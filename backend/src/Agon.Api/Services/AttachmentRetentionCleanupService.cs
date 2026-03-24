using Agon.Api.Configuration;
using Agon.Api.Observability;
using Agon.Application.Interfaces;

namespace Agon.Api.Services;

/// <summary>
/// Periodically removes expired attachment blobs and metadata records.
/// </summary>
public sealed class AttachmentRetentionCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AttachmentOperationsConfiguration _options;
    private readonly ILogger<AttachmentRetentionCleanupService> _logger;

    public AttachmentRetentionCleanupService(
        IServiceScopeFactory scopeFactory,
        AttachmentOperationsConfiguration options,
        ILogger<AttachmentRetentionCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Retention.CleanupEnabled)
        {
            _logger.LogInformation("Attachment retention cleanup is disabled.");
            return;
        }

        var intervalMinutes = Math.Max(1, _options.Retention.CleanupIntervalMinutes);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        _logger.LogInformation(
            "Attachment retention cleanup started (RetentionDays={RetentionDays}, IntervalMinutes={IntervalMinutes}, BatchSize={BatchSize}).",
            _options.Retention.RetentionDays,
            intervalMinutes,
            _options.Retention.CleanupBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Attachment retention cleanup sweep failed unexpectedly.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var attachmentRepository = scope.ServiceProvider.GetService<IAttachmentRepository>();
        var attachmentStorage = scope.ServiceProvider.GetService<IAttachmentStorageService>();

        if (attachmentRepository is null || attachmentStorage is null)
        {
            _logger.LogDebug(
                "Skipping attachment cleanup sweep because dependencies are unavailable (Repository={Repository}, Storage={Storage}).",
                attachmentRepository is not null,
                attachmentStorage is not null);
            return;
        }

        var retentionDays = Math.Max(1, _options.Retention.RetentionDays);
        var batchSize = Math.Clamp(_options.Retention.CleanupBatchSize, 1, 1000);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        var expired = await attachmentRepository.ListExpiredAsync(cutoff, batchSize, cancellationToken);
        if (expired.Count == 0)
        {
            _logger.LogDebug("Attachment cleanup sweep found no expired items (CutoffUtc={CutoffUtc}).", cutoff);
            return;
        }

        var deletedCount = 0;
        var failedCount = 0;

        foreach (var attachment in expired)
        {
            try
            {
                await attachmentStorage.DeleteIfExistsAsync(attachment.BlobName, cancellationToken);
                await attachmentRepository.DeleteAsync(attachment.AttachmentId, cancellationToken);
                deletedCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                AttachmentMetrics.RetentionFailed.Add(1);
                _logger.LogWarning(
                    ex,
                    "Attachment cleanup failed for AttachmentId={AttachmentId}, BlobName={BlobName}, SessionId={SessionId}.",
                    attachment.AttachmentId,
                    attachment.BlobName,
                    attachment.SessionId);
            }
        }

        if (deletedCount > 0)
        {
            AttachmentMetrics.RetentionDeleted.Add(deletedCount);
        }

        _logger.LogInformation(
            "Attachment cleanup sweep complete (ExpiredScanned={ExpiredScanned}, Deleted={Deleted}, Failed={Failed}, CutoffUtc={CutoffUtc}).",
            expired.Count,
            deletedCount,
            failedCount,
            cutoff);
    }
}
