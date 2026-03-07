using TruthMapModel = Agon.Domain.TruthMap.TruthMap;
using Agon.Domain.TruthMap;

namespace Agon.Application.Interfaces;

/// <summary>
/// Abstraction over the Truth Map persistence store (PostgreSQL JSONB + entity tables).
/// The Application layer reads and writes the Truth Map exclusively through this interface.
/// </summary>
public interface ITruthMapRepository
{
    /// <summary>Returns the current Truth Map for the given session.</summary>
    Task<TruthMapModel?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a validated patch to the Truth Map, increments its version,
    /// appends the patch to the event log, and returns the updated Truth Map.
    /// </summary>
    Task<TruthMapModel> ApplyPatchAsync(
        Guid sessionId,
        TruthMapPatch patch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the Truth Map outright (used for seeding a new session or fork initialisation).
    /// Does NOT increment version — caller is responsible for setting the correct version.
    /// </summary>
    Task SaveAsync(TruthMapModel truthMap, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all entity IDs that are transitively downstream of the given entity ID,
    /// using the persisted <c>derived_from</c> graph.
    /// </summary>
    Task<IReadOnlySet<string>> GetImpactSetAsync(
        Guid sessionId,
        string entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full patch history for a session in chronological order.
    /// Used for audit trails and Pause-and-Replay snapshot reconstruction.
    /// </summary>
    Task<IReadOnlyList<TruthMapPatch>> GetPatchHistoryAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
