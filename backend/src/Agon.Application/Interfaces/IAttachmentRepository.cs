using Agon.Application.Models;

namespace Agon.Application.Interfaces;

/// <summary>
/// Persistence operations for session attachment metadata.
/// </summary>
public interface IAttachmentRepository
{
    Task<SessionAttachment> CreateAsync(SessionAttachment attachment, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionAttachment>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
