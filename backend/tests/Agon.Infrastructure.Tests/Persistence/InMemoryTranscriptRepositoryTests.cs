using Agon.Application.Sessions;
using Agon.Infrastructure.Persistence.InMemory;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agon.Infrastructure.Tests.Persistence;

public class InMemoryTranscriptRepositoryTests
{
    [Fact]
    public async Task AppendAndGetBySessionAsync_ReturnsMessagesInInsertionOrder()
    {
        var sessionId = Guid.NewGuid();
        var repository = new InMemoryTranscriptRepository(NullLogger<InMemoryTranscriptRepository>.Instance);

        var first = new TranscriptMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Type = TranscriptMessageType.System,
            Content = "Session started",
            Round = 1,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var second = new TranscriptMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Type = TranscriptMessageType.Agent,
            AgentId = "socratic-clarifier",
            Content = "Kickoff transcript",
            Round = 1,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await repository.AppendAsync(sessionId, first, CancellationToken.None);
        await repository.AppendAsync(sessionId, second, CancellationToken.None);

        var stored = await repository.GetBySessionAsync(sessionId, CancellationToken.None);

        stored.Should().HaveCount(2);
        stored[0].Should().BeEquivalentTo(first);
        stored[1].Should().BeEquivalentTo(second);
    }
}
