using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Agon.Infrastructure.SignalR;
using FluentAssertions;

namespace Agon.Infrastructure.Tests.SignalR;

public class NoOpEventBroadcasterTests
{
    [Fact]
    public async Task RoundProgressAsync_CompletesWithoutThrowing()
    {
        var sut = new NoOpEventBroadcaster();

        var act = () => sut.RoundProgressAsync(Guid.NewGuid(), SessionPhase.DebateRound1, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TruthMapPatchedAsync_CompletesWithoutThrowing()
    {
        var sut = new NoOpEventBroadcaster();
        var patch = new TruthMapPatch
        {
            Meta = new PatchMeta
            {
                Agent = "contrarian",
                Round = 1,
                SessionId = Guid.NewGuid()
            }
        };

        var act = () => sut.TruthMapPatchedAsync(Guid.NewGuid(), patch, version: 1, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TranscriptMessageAppendedAsync_CompletesWithoutThrowing()
    {
        var sut = new NoOpEventBroadcaster();
        var message = new TranscriptMessage
        {
            Id = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            Type = TranscriptMessageType.System,
            Content = "round complete",
            Round = 1,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var act = () => sut.TranscriptMessageAppendedAsync(Guid.NewGuid(), message, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
