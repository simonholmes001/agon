using Agon.Application.Interfaces;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;

namespace Agon.Infrastructure.SignalR;

public class NoOpEventBroadcaster : IEventBroadcaster
{
    public Task RoundProgressAsync(Guid sessionId, SessionPhase phase, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task TruthMapPatchedAsync(Guid sessionId, TruthMapPatch patch, int version, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
