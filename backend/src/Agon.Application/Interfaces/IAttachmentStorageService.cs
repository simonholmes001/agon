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

    Task<Stream?> OpenReadAsync(
        string blobName,
        CancellationToken cancellationToken = default);

    Task DeleteIfExistsAsync(
        string blobName,
        CancellationToken cancellationToken = default);
}

public sealed record AttachmentUploadResult(
    string BlobName,
    string BlobUri,
    string AccessUrl);
