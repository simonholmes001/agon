using Agon.Application.Models;
using Agon.Domain.Sessions;
using Agon.Domain.Snapshots;

namespace Agon.Application.Services;

/// <summary>
/// Abstraction over <see cref="SessionService"/> to allow test isolation in the Orchestrator.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Creates a new session with user context and initial idea.
    /// </summary>
    Task<SessionState> CreateAsync(
        Guid userId,
        string idea,
        int frictionLevel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new session with explicit research tools flag (legacy method).
    /// </summary>
    Task<SessionState> CreateAsync(
        int frictionLevel,
        bool researchToolsEnabled,
        CancellationToken cancellationToken = default);

    Task<SessionState?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins the clarification phase for a session.
    /// </summary>
    Task StartClarificationAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task AdvancePhaseAsync(
        SessionState state,
        SessionPhase nextPhase,
        CancellationToken cancellationToken = default);

    Task RecordRoundSnapshotAsync(
        SessionState state,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionSnapshot>> ListSnapshotsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
