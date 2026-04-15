namespace Agon.Application.Models;

/// <summary>
/// Metadata for an uploaded session attachment (document/image) stored in blob storage.
/// </summary>
public sealed record SessionAttachment(
    Guid AttachmentId,
    Guid SessionId,
    Guid UserId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string BlobName,
    string BlobUri,
    string AccessUrl,
    string? ExtractedText,
    DateTimeOffset UploadedAt,
    string ExtractionStatus = AttachmentExtractionStatus.Ready,
    int ExtractionProgressPercent = 100,
    string? ExtractionError = null,
    DateTimeOffset? ExtractionUpdatedAt = null);
