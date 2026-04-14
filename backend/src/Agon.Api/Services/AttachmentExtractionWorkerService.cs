using System.Threading.Channels;
using Agon.Api.Observability;
using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Services;

namespace Agon.Api.Services;

public sealed record AttachmentExtractionJob(
    Guid AttachmentId,
    Guid SessionId,
    string BlobName,
    string FileName,
    string ContentType,
    long SizeBytes,
    int MaxAllowedBytes);

public sealed class AttachmentAsyncExtractionOptions
{
    public bool Enabled { get; init; } = true;
    public int QueueCapacity { get; init; } = 200;
}

public interface IAttachmentExtractionJobQueue
{
    ValueTask EnqueueAsync(AttachmentExtractionJob job, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AttachmentExtractionJob> DequeueAllAsync(CancellationToken cancellationToken = default);
}

public sealed class AttachmentExtractionJobQueue : IAttachmentExtractionJobQueue
{
    private readonly Channel<AttachmentExtractionJob> _channel;

    public AttachmentExtractionJobQueue(AttachmentAsyncExtractionOptions options)
    {
        var queueCapacity = Math.Max(1, options.QueueCapacity);
        _channel = Channel.CreateBounded<AttachmentExtractionJob>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public async ValueTask EnqueueAsync(AttachmentExtractionJob job, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(job, cancellationToken);
    }

    public IAsyncEnumerable<AttachmentExtractionJob> DequeueAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}

public sealed class AttachmentExtractionWorkerService : BackgroundService
{
    private readonly IAttachmentExtractionJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AttachmentAsyncExtractionOptions _options;
    private readonly ILogger<AttachmentExtractionWorkerService> _logger;

    public AttachmentExtractionWorkerService(
        IAttachmentExtractionJobQueue queue,
        IServiceScopeFactory scopeFactory,
        AttachmentAsyncExtractionOptions options,
        ILogger<AttachmentExtractionWorkerService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Attachment async extraction worker is disabled.");
            return;
        }

        _logger.LogInformation(
            "Attachment async extraction worker started (QueueCapacity={QueueCapacity}).",
            _options.QueueCapacity);

        await foreach (var job in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AttachmentMetrics.ExtractionFailure.Add(1);
                _logger.LogError(
                    ex,
                    "Unhandled exception while processing attachment extraction job (AttachmentId={AttachmentId}, SessionId={SessionId}).",
                    job.AttachmentId,
                    job.SessionId);
            }
        }
    }

    private async Task ProcessJobAsync(AttachmentExtractionJob job, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var parser = scope.ServiceProvider.GetRequiredService<IDocumentParser>();
        var storage = scope.ServiceProvider.GetService<IAttachmentStorageService>();

        if (storage is null)
        {
            AttachmentMetrics.ExtractionFailure.Add(1);
            await PersistFailureAsync(
                sessionService,
                job.AttachmentId,
                "Attachment storage is unavailable for extraction.",
                cancellationToken);
            return;
        }

        await sessionService.UpdateAttachmentExtractionStateAsync(
            job.AttachmentId,
            AttachmentExtractionStatus.Extracting,
            extractedText: null,
            extractionFailureReason: null,
            cancellationToken);

        byte[] content;
        try
        {
            await using var stream = await storage.OpenReadAsync(job.BlobName, cancellationToken);
            if (stream is null)
            {
                AttachmentMetrics.ExtractionFailure.Add(1);
                await PersistFailureAsync(
                    sessionService,
                    job.AttachmentId,
                    "Attachment content is not available for extraction.",
                    cancellationToken);
                return;
            }

            await using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, cancellationToken);
            content = copy.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AttachmentMetrics.ExtractionFailure.Add(1);
            _logger.LogWarning(
                ex,
                "Failed to read attachment content for extraction (AttachmentId={AttachmentId}, BlobName={BlobName}).",
                job.AttachmentId,
                job.BlobName);
            await PersistFailureAsync(
                sessionService,
                job.AttachmentId,
                "Attachment content could not be loaded for extraction.",
                cancellationToken);
            return;
        }

        try
        {
            var parseResult = await parser.ParseAsync(
                new DocumentParseRequest(
                    content,
                    job.FileName,
                    job.ContentType,
                    job.SizeBytes,
                    job.MaxAllowedBytes),
                cancellationToken);

            if (parseResult.Success && !string.IsNullOrWhiteSpace(parseResult.ExtractedText))
            {
                await sessionService.UpdateAttachmentExtractionStateAsync(
                    job.AttachmentId,
                    AttachmentExtractionStatus.Ready,
                    parseResult.ExtractedText,
                    extractionFailureReason: null,
                    cancellationToken);
                return;
            }

            AttachmentMetrics.ExtractionFailure.Add(1);
            await PersistFailureAsync(
                sessionService,
                job.AttachmentId,
                parseResult.FailureReason ?? "Attachment extraction failed.",
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AttachmentMetrics.ExtractionFailure.Add(1);
            _logger.LogWarning(
                ex,
                "Attachment extraction failed (AttachmentId={AttachmentId}, SessionId={SessionId}, FileName={FileName}).",
                job.AttachmentId,
                job.SessionId,
                job.FileName);
            await PersistFailureAsync(
                sessionService,
                job.AttachmentId,
                "Attachment extraction failed.",
                cancellationToken);
        }
    }

    private static async Task PersistFailureAsync(
        ISessionService sessionService,
        Guid attachmentId,
        string reason,
        CancellationToken cancellationToken)
    {
        await sessionService.UpdateAttachmentExtractionStateAsync(
            attachmentId,
            AttachmentExtractionStatus.Failed,
            extractedText: null,
            extractionFailureReason: reason,
            cancellationToken);
    }
}
