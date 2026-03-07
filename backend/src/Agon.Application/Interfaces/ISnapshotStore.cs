using Agon.Domain.Snapshots;

namespace Agon.Application.Interfaces;

/// <summary>
/// Abstraction over immutable snapshot persistence (Blob storage + hash reference in PostgreSQL).
/// Snapshots are written at the end of each round and are never modified after creation.
/// </summary>
public interface ISnapshotStore
{
    /// <summary>
    /// Persists an immutable snapshot and returns it with any storage-assigned metadata.
    /// </summary>
    Task<SessionSnapshot> SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the snapshot with the given ID, or null if not found.
    /// </summary>
    Task<SessionSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all snapshots for a session, ordered by round ascending.
    /// </summary>
    Task<IReadOnlyList<SessionSnapshot>> ListBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
