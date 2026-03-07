using Agon.Application.Interfaces;
using Agon.Domain.Snapshots;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Application.Services;

/// <summary>
/// Application-layer service for snapshot queries and fork context construction.
///
/// Responsibilities:
/// - Retrieving snapshots by ID.
/// - Finding the latest snapshot for a session.
/// - Verifying snapshot integrity via hash check.
/// - Building the initial Truth Map for a forked session.
/// </summary>
public sealed class SnapshotService
{
    private readonly ISnapshotStore _snapshotStore;

    public SnapshotService(ISnapshotStore snapshotStore)
    {
        _snapshotStore = snapshotStore;
    }

    /// <summary>Returns the snapshot with the highest round number for the given session.</summary>
    public async Task<SessionSnapshot?> GetLatestAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await _snapshotStore.ListBySessionAsync(sessionId, cancellationToken);
        return snapshots.Count == 0
            ? null
            : snapshots.MaxBy(s => s.Round);
    }

    /// <summary>Returns the snapshot with the given ID, or null if not found.</summary>
    public Task<SessionSnapshot?> GetByIdAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default)
        => _snapshotStore.GetAsync(snapshotId, cancellationToken);

    /// <summary>
    /// Verifies the SHA-256 hash of a stored snapshot against its payload.
    /// Returns false if the snapshot is not found or its hash does not match.
    /// </summary>
    public async Task<bool> VerifyIntegrityAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshotStore.GetAsync(snapshotId, cancellationToken);
        return snapshot?.IsIntact() ?? false;
    }

    /// <summary>
    /// Returns all snapshots for the session, sorted by round ascending.
    /// </summary>
    public async Task<IReadOnlyList<SessionSnapshot>> ListAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await _snapshotStore.ListBySessionAsync(sessionId, cancellationToken);
        return [.. snapshots.OrderBy(s => s.Round)];
    }

    /// <summary>
    /// Returns the Truth Map that a forked session should start from.
    /// Loads the snapshot referenced by <see cref="ForkRequest.SnapshotId"/>.
    /// Returns null if the snapshot is not found.
    ///
    /// Note: Applying <see cref="ForkRequest.InitialPatches"/> onto the returned Truth Map
    /// is the responsibility of the caller (the Orchestrator) so that patch validation
    /// is applied through the normal <c>AgentRunner</c> pipeline.
    /// </summary>
    public async Task<TruthMapModel?> BuildForkContextAsync(
        ForkRequest forkRequest,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshotStore.GetAsync(forkRequest.SnapshotId, cancellationToken);
        return snapshot?.TruthMap;
    }
}
