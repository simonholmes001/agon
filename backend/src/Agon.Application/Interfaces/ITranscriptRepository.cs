using Agon.Application.Sessions;

namespace Agon.Application.Interfaces;

public interface ITranscriptRepository
{
    Task AppendAsync(Guid sessionId, TranscriptMessage message, CancellationToken cancellationToken);

    Task<IReadOnlyList<TranscriptMessage>> GetBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}
