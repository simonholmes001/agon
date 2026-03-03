using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Infrastructure.Persistence.InMemory;
using FluentAssertions;

namespace Agon.Infrastructure.Tests.Persistence;

public class InMemorySessionRepositoryTests
{
    [Fact]
    public async Task CreateAndGetAsync_ReturnsStoredSession()
    {
        var repository = new InMemorySessionRepository();
        var session = new SessionState
        {
            SessionId = Guid.NewGuid(),
            Mode = SessionMode.Deep,
            Status = SessionStatus.Active,
            Phase = SessionPhase.Clarification,
            FrictionLevel = 60,
            RoundPolicy = new RoundPolicy()
        };

        await repository.CreateAsync(session, CancellationToken.None);

        var loaded = await repository.GetAsync(session.SessionId, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.SessionId.Should().Be(session.SessionId);
        loaded.Phase.Should().Be(SessionPhase.Clarification);
    }

    [Fact]
    public async Task UpdateAsync_PersistsNewState()
    {
        var repository = new InMemorySessionRepository();
        var session = new SessionState
        {
            SessionId = Guid.NewGuid(),
            Mode = SessionMode.Deep,
            Status = SessionStatus.Active,
            Phase = SessionPhase.Clarification,
            FrictionLevel = 60,
            RoundPolicy = new RoundPolicy()
        };

        await repository.CreateAsync(session, CancellationToken.None);
        session.Phase = SessionPhase.Construction;

        await repository.UpdateAsync(session, CancellationToken.None);

        var loaded = await repository.GetAsync(session.SessionId, CancellationToken.None);
        loaded!.Phase.Should().Be(SessionPhase.Construction);
    }
}
