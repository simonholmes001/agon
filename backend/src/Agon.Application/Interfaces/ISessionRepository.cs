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

    /// <summary>Persists changes to an existing session (phase transitions, counters, status).</summary>
    Task UpdateAsync(SessionState sessionState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all sessions belonging to the given user.
    /// Results are ordered by <c>created_at</c> descending.
    /// </summary>
    Task<IReadOnlyList<SessionState>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
