using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Agon.Application.Sessions;

namespace Agon.Application.Interfaces;

public interface IEventBroadcaster
{
    Task RoundProgressAsync(Guid sessionId, SessionPhase phase, CancellationToken cancellationToken);
    Task TruthMapPatchedAsync(Guid sessionId, TruthMapPatch patch, int version, CancellationToken cancellationToken);
    Task TranscriptMessageAppendedAsync(Guid sessionId, TranscriptMessage message, CancellationToken cancellationToken);
}
