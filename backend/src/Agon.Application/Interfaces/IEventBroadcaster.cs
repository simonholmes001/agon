using Agon.Domain.TruthMap.Entities;

namespace Agon.Application.Interfaces;

/// <summary>
/// Abstraction over the SignalR hub for real-time event broadcasting.
/// The Application layer calls this to push events to connected frontend clients.
/// The Infrastructure layer implements this via <c>DebateHub</c>.
/// </summary>
public interface IEventBroadcaster
{
    /// <summary>Streams a partial agent output token to the UI.</summary>
    Task SendTokenAsync(
        Guid sessionId,
        string agentId,
        string token,
        bool isComplete,
        CancellationToken cancellationToken = default);

    /// <summary>Notifies the UI of a phase transition (e.g., CLARIFICATION → ANALYSIS_ROUND).</summary>
    Task SendRoundProgressAsync(
        Guid sessionId,
        string phase,
        string status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a Truth Map patch event after it has been validated and applied.
    /// The payload includes the applied patch and the new Truth Map version.
    /// </summary>
    Task SendTruthMapPatchAsync(
        Guid sessionId,
        object patch,
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a confidence transition (e.g., claim decayed or boosted).
    /// </summary>
    Task SendConfidenceTransitionAsync(
        Guid sessionId,
        ConfidenceTransition transition,
        CancellationToken cancellationToken = default);

    /// <summary>Broadcasts the latest convergence scores after a round.</summary>
    Task SendConvergenceUpdateAsync(
        Guid sessionId,
        object convergence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts the IDs of entities marked as Pending Revalidation
    /// after a mid-session constraint change triggers change propagation.
    /// </summary>
    Task SendPendingRevalidationAsync(
        Guid sessionId,
        IReadOnlyList<string> entityIds,
        CancellationToken cancellationToken = default);

    /// <summary>Broadcasts a budget warning when the session reaches 80% or 95% budget usage.</summary>
    Task SendBudgetWarningAsync(
        Guid sessionId,
        float percentUsed,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>Notifies the UI that an artifact has been generated or regenerated.</summary>
    Task SendArtifactReadyAsync(
        Guid sessionId,
        string artifactType,
        int version,
        CancellationToken cancellationToken = default);
}
