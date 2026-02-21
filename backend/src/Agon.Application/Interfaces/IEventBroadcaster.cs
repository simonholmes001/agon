using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;

namespace Agon.Application.Interfaces;

public interface IEventBroadcaster
{
    Task RoundProgressAsync(Guid sessionId, SessionPhase phase, CancellationToken cancellationToken);
    Task TruthMapPatchedAsync(Guid sessionId, TruthMapPatch patch, int version, CancellationToken cancellationToken);
}
