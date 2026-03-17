namespace Agon.Application.Interfaces;

/// <summary>
/// Extracts usable text from uploaded attachments.
/// Implementations can use local parsing, OCR, document intelligence, or vision models.
/// </summary>
public interface IAttachmentTextExtractor
{
    Task<string?> ExtractAsync(
        byte[] content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
