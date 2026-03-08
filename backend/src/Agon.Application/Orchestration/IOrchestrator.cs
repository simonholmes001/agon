using Agon.Application.Models;

namespace Agon.Application.Orchestration;

/// <summary>
/// Interface for the deterministic session state machine that controls all phase transitions.
/// </summary>
public interface IOrchestrator
{
    /// <summary>
    /// Called when a new session is initiated (INTAKE).
    /// Seeds the Truth Map and transitions to CLARIFICATION.
    /// </summary>
    Task StartSessionAsync(SessionState state, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the Moderator agent for the Clarification phase.
    /// The Moderator either asks clarifying questions or signals READY.
    /// </summary>
    Task RunModeratorAsync(SessionState state, CancellationToken cancellationToken);
}
