using Agon.Application.Models;
using Agon.Domain.Sessions;

namespace Agon.Application.Interfaces;

/// <summary>
/// Abstraction over session record persistence (PostgreSQL sessions table).
/// Stores the session lifecycle state — not the Truth Map (see <see cref="ITruthMapRepository"/>).
/// </summary>
public interface ISessionRepository
{
    /// <summary>Creates a new session record and returns the persisted state.</summary>
    Task<SessionState> CreateAsync(SessionState sessionState, CancellationToken cancellationToken = default);

    /// <summary>Returns the session state for the given ID, or null if not found.</summary>
    Task<SessionState?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the session state for the given ID only when it belongs to the specified user.
    /// Returns null if the session does not exist <em>or</em> is not owned by <paramref name="userId"/>.
    /// This is the preferred method for all user-facing reads because it provides defense-in-depth
    /// isolation at the persistence layer — ownership is enforced even if controller logic changes.
    /// </summary>
    Task<SessionState?> GetByUserAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing session (phase transitions, counters, status).</summary>
    Task UpdateAsync(SessionState sessionState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists council-run metadata fields only (phase/timestamps/failure reason) without mutating
    /// broader session lifecycle fields such as phase/status/round/token counters.
    /// </summary>
    Task UpdateCouncilRunMetadataAsync(SessionState sessionState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all sessions belonging to the given user.
    /// Results are ordered by <c>created_at</c> descending.
    /// </summary>
    Task<IReadOnlyList<SessionState>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
