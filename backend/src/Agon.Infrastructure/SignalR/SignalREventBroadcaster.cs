using Agon.Application.Interfaces;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Agon.Infrastructure.SignalR;

public class SignalREventBroadcaster(
    IHubContext<DebateHub> hubContext,
    ILogger<SignalREventBroadcaster> logger) : IEventBroadcaster
{
    public async Task RoundProgressAsync(Guid sessionId, SessionPhase phase, CancellationToken cancellationToken)
    {
        try
        {
            await hubContext.Clients
                .Group(DebateHub.SessionGroupName(sessionId))
                .SendAsync("RoundProgress", new RoundProgressEvent(sessionId, phase.ToString()), cancellationToken);

            logger.LogInformation(
                "Broadcast RoundProgress event. SessionId={SessionId} Phase={Phase}",
                sessionId,
                phase);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to broadcast RoundProgress event. SessionId={SessionId} Phase={Phase}",
                sessionId,
                phase);
        }
    }

    public async Task TruthMapPatchedAsync(Guid sessionId, TruthMapPatch patch, int version, CancellationToken cancellationToken)
    {
        try
        {
            await hubContext.Clients
                .Group(DebateHub.SessionGroupName(sessionId))
                .SendAsync("TruthMapPatch", new TruthMapPatchEvent(sessionId, version, patch), cancellationToken);

            logger.LogInformation(
                "Broadcast TruthMapPatch event. SessionId={SessionId} Version={Version} Agent={Agent}",
                sessionId,
                version,
                patch.Meta.Agent);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to broadcast TruthMapPatch event. SessionId={SessionId} Version={Version} Agent={Agent}",
                sessionId,
                version,
                patch.Meta.Agent);
        }
    }

    private sealed record RoundProgressEvent(Guid SessionId, string Phase);

    private sealed record TruthMapPatchEvent(Guid SessionId, int Version, TruthMapPatch Patch);
}
