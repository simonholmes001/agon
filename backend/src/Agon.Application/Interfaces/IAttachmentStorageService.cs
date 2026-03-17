namespace Agon.Application.Interfaces;

/// <summary>
/// Blob storage abstraction for uploaded attachments.
/// </summary>
public interface IAttachmentStorageService
{
    Task<AttachmentUploadResult> UploadAsync(
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);
}

public sealed record AttachmentUploadResult(
    string BlobName,
    string BlobUri,
    string AccessUrl);
