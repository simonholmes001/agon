namespace Agon.Api.Configuration;

/// <summary>
/// Configuration section for attachment storage access.
/// </summary>
public sealed class StorageConfiguration
{
    public const string SectionName = "Storage";

    /// <summary>
    /// When true, backend uses managed identity against AttachmentBlobServiceUri.
    /// When false, backend uses ConnectionStrings:BlobStorage.
    /// </summary>
    public bool UseManagedIdentity { get; set; } = false;

    /// <summary>
    /// Blob service endpoint (for example: https://mystorage.blob.core.windows.net).
    /// Required when UseManagedIdentity=true.
    /// </summary>
    public string AttachmentBlobServiceUri { get; set; } = string.Empty;

    /// <summary>
    /// Blob container used for session attachments.
    /// </summary>
    public string AttachmentContainer { get; set; } = "session-attachments";
}
