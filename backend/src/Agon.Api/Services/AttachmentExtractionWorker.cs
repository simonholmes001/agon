using Agon.Application.Models;
using Agon.Application.Services;

namespace Agon.Api.Services;

public sealed class AttachmentExtractionWorker : BackgroundService
{
    private const string RecoveryReasonCode = "RECOVERY_SWEEP";
    private const string QueueFailureReasonCode = "QUEUE_PROCESS_FAILURE";
    private const string WorkerFailureMessage = "Extraction failed due to an internal processing error.";

    private readonly IAttachmentExtractionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AttachmentExtractionQueueOptions _options;
    private readonly ILogger<AttachmentExtractionWorker> _logger;

    public AttachmentExtractionWorker(
        IAttachmentExtractionQueue queue,
        IServiceScopeFactory scopeFactory,
        AttachmentExtractionQueueOptions options,
        ILogger<AttachmentExtractionWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverPendingAttachmentsAsync(stoppingToken);

        var drainTask = DrainQueueAsync(stoppingToken);
        var recoveryTask = RecoveryLoopAsync(stoppingToken);

        await Task.WhenAll(drainTask, recoveryTask);
    }

    private async Task DrainQueueAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            SessionAttachment attachment;
            try
            {
                attachment = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<AttachmentExtractionProcessor>();
                await processor.ProcessAsync(attachment, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Attachment extraction worker failure for AttachmentId={AttachmentId}, SessionId={SessionId}, ReasonCode={ReasonCode}",
                    attachment.AttachmentId,
                    attachment.SessionId,
                    QueueFailureReasonCode);

                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
                    await sessionService.UpdateAttachmentExtractionStateAsync(
                        attachment.AttachmentId,
                        AttachmentExtractionStatus.Failed,
                        extractedText: null,
                        WorkerFailureMessage,
                        CancellationToken.None);
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(
                        updateEx,
                        "Unable to persist failed extraction status for AttachmentId={AttachmentId}, SessionId={SessionId}",
                        attachment.AttachmentId,
                        attachment.SessionId);
                }
            }
        }
    }

    private async Task RecoveryLoopAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Max(5, _options.RecoveryIntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RecoverPendingAttachmentsAsync(stoppingToken);
        }
    }

    private async Task RecoverPendingAttachmentsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
            var candidates = await sessionService.ListPendingAttachmentExtractionsAsync(
                Math.Max(1, _options.RecoveryBatchSize),
                cancellationToken);

            foreach (var attachment in candidates)
            {
                if (!_queue.TryQueue(attachment))
                {
                    _logger.LogWarning(
                        "Attachment recovery queue is full; pending attachment remains queued for retry. AttachmentId={AttachmentId}, SessionId={SessionId}, ReasonCode={ReasonCode}",
                        attachment.AttachmentId,
                        attachment.SessionId,
                        RecoveryReasonCode);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // no-op on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attachment extraction recovery scan failed.");
        }
    }
}
