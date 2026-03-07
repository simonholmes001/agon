using Agon.Application.Interfaces;
using Agon.Domain.TruthMap.Entities;
using Microsoft.AspNetCore.SignalR;

namespace Agon.Infrastructure.SignalR;

/// <summary>
/// SignalR-based implementation of IEventBroadcaster.
/// Sends real-time events to frontend clients connected to the DebateHub.
/// Uses session-based groups to target specific debate sessions.
/// </summary>
public sealed class SignalREventBroadcaster : IEventBroadcaster
{
    private readonly IHubContext<DebateHub> _hubContext;

    public SignalREventBroadcaster(IHubContext<DebateHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendTokenAsync(
        Guid sessionId,
        string agentId,
        string token,
        bool isComplete,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync("AgentToken", agentId, token, isComplete, cancellationToken);
    }

    public async Task SendRoundProgressAsync(
        Guid sessionId,
        string phase,
        string status,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync("RoundProgress", phase, status, cancellationToken);
    }

    public async Task SendTruthMapPatchAsync(
        Guid sessionId,
        object patch,
        int version,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync("TruthMapPatch", patch, version, cancellationToken);
    }

    public async Task SendConfidenceTransitionAsync(
        Guid sessionId,
        ConfidenceTransition transition,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync("ConfidenceTransition", transition, cancellationToken);
    }

    public async Task SendConvergenceUpdateAsync(
        Guid sessionId,
        object convergence,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync("ConvergenceUpdate", convergence, cancellationToken);
    }

    public async Task SendPendingRevalidationAsync(
        Guid sessionId,
        IReadOnlyList<string> entityIds,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync("PendingRevalidation", entityIds, cancellationToken);
    }

    public async Task SendBudgetWarningAsync(
        Guid sessionId,
        float percentUsed,
        string message,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync("BudgetWarning", percentUsed, message, cancellationToken);
    }

    public async Task SendArtifactReadyAsync(
        Guid sessionId,
        string artifactType,
        int version,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync("ArtifactReady", artifactType, version, cancellationToken);
    }
}
