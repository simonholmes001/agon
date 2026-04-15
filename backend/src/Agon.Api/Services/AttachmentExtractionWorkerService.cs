using Agon.Api.Observability;
using Agon.Api.Configuration;
using Agon.Application.Attachments;
using Agon.Application.Interfaces;
using Agon.Application.Models;

namespace Agon.Api.Services;

public sealed class AttachmentAsyncExtractionOptions
{
    public bool Enabled { get; init; } = true;
    public int BatchSize { get; init; } = 20;
    public int PollIntervalMs { get; init; } = 1000;
    public bool RequeueStaleExtractingEnabled { get; init; } = true;
    public int StaleExtractingAfterMinutes { get; init; } = 15;
    public int ReconcileIntervalMs { get; init; } = 30000;
}

public sealed class AttachmentExtractionWorkerService : BackgroundService
{
    private const string RequeuedExtractionReason = "Attachment extraction was requeued after stale processing timeout.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AttachmentAsyncExtractionOptions _options;
    private readonly AttachmentUploadValidationOptions _uploadValidationOptions;
    private readonly ILogger<AttachmentExtractionWorkerService> _logger;

    public AttachmentExtractionWorkerService(
        IServiceScopeFactory scopeFactory,
        AttachmentAsyncExtractionOptions options,
        AttachmentUploadValidationOptions uploadValidationOptions,
        ILogger<AttachmentExtractionWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _uploadValidationOptions = uploadValidationOptions;
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
            "Attachment async extraction worker started (BatchSize={BatchSize}, PollIntervalMs={PollIntervalMs}, StaleRequeueEnabled={StaleEnabled}).",
            _options.BatchSize,
            _options.PollIntervalMs,
            _options.RequeueStaleExtractingEnabled);

        var lastReconcileAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (ShouldRunReconcile(lastReconcileAt))
                {
                    await RequeueStaleExtractingAttachmentsAsync(stoppingToken);
                    lastReconcileAt = DateTimeOffset.UtcNow;
                }

                var processedAny = await ProcessUploadedAttachmentsAsync(stoppingToken);
                if (!processedAny)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(100, _options.PollIntervalMs)), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in attachment async extraction worker loop.");
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(250, _options.PollIntervalMs)), stoppingToken);
            }
        }
    }

    private bool ShouldRunReconcile(DateTimeOffset lastReconcileAt)
    {
        if (!_options.RequeueStaleExtractingEnabled)
        {
            return false;
        }

        var intervalMs = Math.Max(1000, _options.ReconcileIntervalMs);
        return DateTimeOffset.UtcNow - lastReconcileAt >= TimeSpan.FromMilliseconds(intervalMs);
    }

    private async Task<bool> ProcessUploadedAttachmentsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAttachmentRepository>();
        var parser = scope.ServiceProvider.GetRequiredService<IDocumentParser>();
        var storage = scope.ServiceProvider.GetService<IAttachmentStorageService>();

        if (storage is null)
        {
            _logger.LogWarning("Attachment storage is unavailable. Skipping async extraction batch.");
            return false;
        }

        var batchSize = Math.Max(1, _options.BatchSize);
        var pending = await repository.ListByExtractionStatusAsync(
            AttachmentExtractionStatus.Uploaded,
            batchSize,
            cancellationToken: cancellationToken);
        if (pending.Count == 0)
        {
            return false;
        }

        var processedAny = false;
        foreach (var attachment in pending)
        {
            var claimed = await repository.TryTransitionExtractionStateAsync(
                attachment.AttachmentId,
                AttachmentExtractionStatus.Uploaded,
                AttachmentExtractionStatus.Extracting,
                extractedText: null,
                extractionFailureReason: null,
                cancellationToken);
            if (!claimed)
            {
                continue;
            }

            processedAny = true;
            await ProcessClaimedAttachmentAsync(repository, parser, storage, attachment, cancellationToken);
        }

        return processedAny;
    }

    private async Task ProcessClaimedAttachmentAsync(
        IAttachmentRepository repository,
        IDocumentParser parser,
        IAttachmentStorageService storage,
        SessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        byte[] content;
        try
        {
            await using var stream = await storage.OpenReadAsync(attachment.BlobName, cancellationToken);
            if (stream is null)
            {
                AttachmentMetrics.ExtractionFailure.Add(1);
                await MarkClaimedAttachmentFailedAsync(
                    repository,
                    attachment.AttachmentId,
                    "Attachment content is not available for extraction.",
                    cancellationToken);
                return;
            }

            await using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, cancellationToken);
            content = copy.ToArray();
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
                "Failed to read attachment content for extraction (AttachmentId={AttachmentId}, BlobName={BlobName}).",
                attachment.AttachmentId,
                attachment.BlobName);
            await MarkClaimedAttachmentFailedAsync(
                repository,
                attachment.AttachmentId,
                "Attachment content could not be loaded for extraction.",
                cancellationToken);
            return;
        }

        try
        {
            var maxAllowedBytes = ResolveMaxAllowedBytes(attachment.FileName, attachment.ContentType);
            var parseResult = await parser.ParseAsync(
                new DocumentParseRequest(
                    content,
                    attachment.FileName,
                    attachment.ContentType,
                    attachment.SizeBytes,
                    maxAllowedBytes),
                cancellationToken);

            if (parseResult.Success && !string.IsNullOrWhiteSpace(parseResult.ExtractedText))
            {
                var movedToReady = await repository.TryTransitionExtractionStateAsync(
                    attachment.AttachmentId,
                    AttachmentExtractionStatus.Extracting,
                    AttachmentExtractionStatus.Ready,
                    parseResult.ExtractedText,
                    extractionFailureReason: null,
                    cancellationToken);
                if (!movedToReady)
                {
                    _logger.LogInformation(
                        "Attachment extraction ready transition skipped because state changed concurrently (AttachmentId={AttachmentId}).",
                        attachment.AttachmentId);
                }

                return;
            }

            AttachmentMetrics.ExtractionFailure.Add(1);
            await MarkClaimedAttachmentFailedAsync(
                repository,
                attachment.AttachmentId,
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
                attachment.AttachmentId,
                attachment.SessionId,
                attachment.FileName);
            await MarkClaimedAttachmentFailedAsync(
                repository,
                attachment.AttachmentId,
                "Attachment extraction failed.",
                cancellationToken);
        }
    }

    private async Task RequeueStaleExtractingAttachmentsAsync(CancellationToken cancellationToken)
    {
        var staleAfterMinutes = Math.Max(1, _options.StaleExtractingAfterMinutes);
        var uploadedBefore = DateTimeOffset.UtcNow.AddMinutes(-staleAfterMinutes);

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAttachmentRepository>();
        var stale = await repository.ListByExtractionStatusAsync(
            AttachmentExtractionStatus.Extracting,
            Math.Max(1, _options.BatchSize),
            uploadedBefore,
            cancellationToken);
        if (stale.Count == 0)
        {
            return;
        }

        var requeuedCount = 0;
        foreach (var attachment in stale)
        {
            var moved = await repository.TryTransitionExtractionStateAsync(
                attachment.AttachmentId,
                AttachmentExtractionStatus.Extracting,
                AttachmentExtractionStatus.Uploaded,
                extractedText: null,
                extractionFailureReason: RequeuedExtractionReason,
                cancellationToken);
            if (moved)
            {
                requeuedCount++;
            }
        }

        if (requeuedCount > 0)
        {
            _logger.LogWarning(
                "Requeued {Count} stale attachments stuck in extracting state.",
                requeuedCount);
        }
    }

    private async Task MarkClaimedAttachmentFailedAsync(
        IAttachmentRepository repository,
        Guid attachmentId,
        string reason,
        CancellationToken cancellationToken)
    {
        var movedToFailed = await repository.TryTransitionExtractionStateAsync(
            attachmentId,
            AttachmentExtractionStatus.Extracting,
            AttachmentExtractionStatus.Failed,
            extractedText: null,
            extractionFailureReason: reason,
            cancellationToken);
        if (!movedToFailed)
        {
            _logger.LogInformation(
                "Attachment failure transition skipped because state changed concurrently (AttachmentId={AttachmentId}).",
                attachmentId);
        }
    }

    private int ResolveMaxAllowedBytes(string fileName, string contentType)
    {
        var normalizedContentType = NormalizeContentType(contentType);
        var route = AttachmentRoutingPolicy.Resolve(fileName, normalizedContentType);

        return route switch
        {
            AttachmentRoutingRoute.Text => _uploadValidationOptions.MaxTextUploadBytes,
            AttachmentRoutingRoute.Document => _uploadValidationOptions.MaxDocumentUploadBytes,
            AttachmentRoutingRoute.Image => _uploadValidationOptions.MaxImageUploadBytes,
            _ => _uploadValidationOptions.MaxUploadBytes
        };
    }

    private static string NormalizeContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        var semicolonIndex = contentType.IndexOf(';');
        return (semicolonIndex >= 0 ? contentType[..semicolonIndex] : contentType)
            .Trim()
            .ToLowerInvariant();
    }
}
