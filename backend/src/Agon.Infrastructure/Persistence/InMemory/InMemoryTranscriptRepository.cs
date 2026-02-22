using Agon.Application.Interfaces;
using Agon.Application.Sessions;
using Microsoft.Extensions.Logging;

namespace Agon.Infrastructure.Persistence.InMemory;

public class InMemoryTranscriptRepository(ILogger<InMemoryTranscriptRepository> logger) : ITranscriptRepository
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, List<TranscriptMessage>> bySession = new();

    public Task AppendAsync(Guid sessionId, TranscriptMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            if (!bySession.TryGetValue(sessionId, out var messages))
            {
                messages = new List<TranscriptMessage>();
                bySession[sessionId] = messages;
            }

            messages.Add(message);
        }

        logger.LogInformation(
            "Stored transcript message. SessionId={SessionId} MessageId={MessageId} Type={Type} Round={Round}",
            sessionId,
            message.Id,
            message.Type,
            message.Round);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TranscriptMessage>> GetBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<TranscriptMessage> snapshot;
        lock (gate)
        {
            if (!bySession.TryGetValue(sessionId, out var messages))
            {
                snapshot = new List<TranscriptMessage>();
            }
            else
            {
                snapshot = messages.ToList();
            }
        }

        logger.LogInformation(
            "Loaded transcript messages. SessionId={SessionId} MessageCount={MessageCount}",
            sessionId,
            snapshot.Count);
        return Task.FromResult<IReadOnlyList<TranscriptMessage>>(snapshot);
    }
}
