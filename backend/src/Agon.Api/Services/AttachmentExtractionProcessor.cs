using Agon.Api.Observability;
using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Services;
using Agon.Infrastructure.Attachments;

namespace Agon.Api.Services;

/// <summary>
/// Runs attachment text extraction outside the request path and persists lifecycle status.
/// </summary>
public sealed class AttachmentExtractionProcessor
{
    private const int ExtractingProgressPercent = 20;
    private const string GenericFailureMessage = "Extraction failed. Unsupported format or processing error.";
    private const string MissingBlobFailureMessage = "Extraction failed because attachment content is unavailable.";

    private readonly ISessionService _sessionService;
    private readonly IAttachmentStorageService _storage;
    private readonly IAttachmentTextExtractor _extractor;
    private readonly AttachmentExtractionOptions _options;
    private readonly ILogger<AttachmentExtractionProcessor> _logger;

    public AttachmentExtractionProcessor(
        ISessionService sessionService,
        IAttachmentStorageService storage,
        IAttachmentTextExtractor extractor,
        AttachmentExtractionOptions options,
        ILogger<AttachmentExtractionProcessor> logger)
    {
        _sessionService = sessionService;
        _storage = storage;
        _extractor = extractor;
        _options = options;
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
            var maxBytes = Math.Max(1, _options.MaxExtractionFileBytes);
            if (attachment.SizeBytes > maxBytes)
            {
                await MarkFailedAsync(
                    attachment,
                    $"Extraction skipped because file exceeds {maxBytes / (1024 * 1024)} MB extraction limit.",
                    cancellationToken);
                return;
            }

            await using var source = await _storage.OpenReadAsync(attachment.BlobName, cancellationToken);
            if (source is null)
            {
                await MarkFailedAsync(attachment, MissingBlobFailureMessage, cancellationToken);
                return;
            }

            var fileBytes = await ReadBytesWithLimitAsync(source, maxBytes, cancellationToken);

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
        catch (AttachmentTooLargeException)
        {
            var maxBytes = Math.Max(1, _options.MaxExtractionFileBytes);
            await MarkFailedAsync(
                attachment,
                $"Extraction skipped because file exceeds {maxBytes / (1024 * 1024)} MB extraction limit.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            await MarkFailedAsync(attachment, GenericFailureMessage, cancellationToken);

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

    private static async Task<byte[]> ReadBytesWithLimitAsync(
        Stream source,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var copy = new MemoryStream();
        var buffer = new byte[81920];
        var totalRead = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > maxBytes)
            {
                throw new AttachmentTooLargeException();
            }

            await copy.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return copy.ToArray();
    }

    private sealed class AttachmentTooLargeException : Exception;
}
