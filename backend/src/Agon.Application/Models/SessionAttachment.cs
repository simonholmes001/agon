namespace Agon.Application.Models;

public enum AttachmentExtractionStatus
{
    Uploaded = 0,
    Extracting = 1,
    Ready = 2,
    Failed = 3
}

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
    AttachmentExtractionStatus ExtractionStatus = AttachmentExtractionStatus.Uploaded,
    string? ExtractionFailureReason = null);
