using Agon.Application.Interfaces;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Agon.Infrastructure.Persistence.AzureBlob;

/// <summary>
/// Azure Blob Storage implementation for session attachments.
/// </summary>
public sealed class AzureBlobAttachmentStorageService : IAttachmentStorageService
{
    private readonly BlobContainerClient _container;
    private readonly StorageSharedKeyCredential? _sharedKeyCredential;

    public AzureBlobAttachmentStorageService(string connectionString, string containerName)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Blob storage connection string is required.", nameof(connectionString));
        }
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new ArgumentException("Blob container name is required.", nameof(containerName));
        }

        _container = new BlobContainerClient(connectionString, containerName);
        _container.CreateIfNotExists(PublicAccessType.None);
        _sharedKeyCredential = TryBuildSharedKey(connectionString);
    }

    public async Task<AttachmentUploadResult> UploadAsync(
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var blobClient = _container.GetBlobClient(blobName);
        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType
                }
            },
            cancellationToken);

        var blobUri = blobClient.Uri.ToString();
        var accessUrl = GenerateReadOnlySasUrl(blobClient) ?? blobUri;

        return new AttachmentUploadResult(blobName, blobUri, accessUrl);
    }

    private string? GenerateReadOnlySasUrl(BlobClient blobClient)
    {
        if (_sharedKeyCredential is null)
        {
            return null;
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = blobClient.Name,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(12)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasToken = sasBuilder.ToSasQueryParameters(_sharedKeyCredential).ToString();
        return $"{blobClient.Uri}?{sasToken}";
    }

    private static StorageSharedKeyCredential? TryBuildSharedKey(string connectionString)
    {
        var segments = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in segments)
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2)
            {
                values[pair[0]] = pair[1];
            }
        }

        if (!values.TryGetValue("AccountName", out var accountName) || string.IsNullOrWhiteSpace(accountName))
        {
            return null;
        }

        if (!values.TryGetValue("AccountKey", out var accountKey) || string.IsNullOrWhiteSpace(accountKey))
        {
            return null;
        }

        return new StorageSharedKeyCredential(accountName, accountKey);
    }
}
