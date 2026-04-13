using Agon.Application.Models;

namespace Agon.Application.Interfaces;

/// <summary>
/// Persistence operations for session attachment metadata.
/// </summary>
public interface IAttachmentRepository
{
    Task<SessionAttachment> CreateAsync(SessionAttachment attachment, CancellationToken cancellationToken = default);

    Task<SessionAttachment> UpdateExtractionStateAsync(
        Guid attachmentId,
        AttachmentExtractionStatus extractionStatus,
        string? extractedText,
        string? extractionFailureReason,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionAttachment>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionAttachment>> ListExpiredAsync(
        DateTimeOffset uploadedBefore,
        int limit,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}
