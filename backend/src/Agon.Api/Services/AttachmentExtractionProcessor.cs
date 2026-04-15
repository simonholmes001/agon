using Agon.Api.Observability;
using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Services;

namespace Agon.Api.Services;

/// <summary>
/// Runs attachment text extraction outside the request path and persists lifecycle status.
/// </summary>
public sealed class AttachmentExtractionProcessor
{
    private const int ExtractingProgressPercent = 20;

    private readonly ISessionService _sessionService;
    private readonly IAttachmentStorageService _storage;
    private readonly IAttachmentTextExtractor _extractor;
    private readonly ILogger<AttachmentExtractionProcessor> _logger;

    public AttachmentExtractionProcessor(
        ISessionService sessionService,
        IAttachmentStorageService storage,
        IAttachmentTextExtractor extractor,
        ILogger<AttachmentExtractionProcessor> logger)
    {
        _sessionService = sessionService;
        _storage = storage;
        _extractor = extractor;
        _logger = logger;
    }

    public async Task ProcessAsync(SessionAttachment attachment, CancellationToken cancellationToken)
    {
        await _sessionService.UpdateAttachmentExtractionAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Extracting,
            ExtractingProgressPercent,
            null,
            null,
            cancellationToken);

        try
        {
            await using var source = await _storage.OpenReadAsync(attachment.BlobName, cancellationToken);
            if (source is null)
            {
                await MarkFailedAsync(
                    attachment,
                    $"Attachment blob '{attachment.BlobName}' could not be found.",
                    cancellationToken);
                return;
            }

            byte[] fileBytes;
            await using (var copy = new MemoryStream())
            {
                await source.CopyToAsync(copy, cancellationToken);
                fileBytes = copy.ToArray();
            }

            var extractedText = await _extractor.ExtractAsync(
                fileBytes,
                attachment.FileName,
                attachment.ContentType,
                cancellationToken);

            await _sessionService.UpdateAttachmentExtractionAsync(
                attachment.AttachmentId,
                AttachmentExtractionStatus.Ready,
                100,
                extractedText,
                null,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await MarkFailedAsync(attachment, ex.Message, cancellationToken);

            AttachmentMetrics.ExtractionFailure.Add(1);
            _logger.LogWarning(
                ex,
                "Attachment extraction failed for AttachmentId={AttachmentId}, SessionId={SessionId}, FileName={FileName}",
                attachment.AttachmentId,
                attachment.SessionId,
                attachment.FileName);
        }
    }

    private async Task MarkFailedAsync(
        SessionAttachment attachment,
        string error,
        CancellationToken cancellationToken)
    {
        await _sessionService.UpdateAttachmentExtractionAsync(
            attachment.AttachmentId,
            AttachmentExtractionStatus.Failed,
            100,
            null,
            string.IsNullOrWhiteSpace(error) ? "Attachment extraction failed." : error,
            cancellationToken);
    }
}
