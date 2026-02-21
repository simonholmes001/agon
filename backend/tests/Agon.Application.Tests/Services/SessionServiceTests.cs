using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Sessions;
using Agon.Application.Services;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using FluentAssertions;
using NSubstitute;

namespace Agon.Application.Tests.Services;

public class SessionServiceTests
{
    [Fact]
    public async Task CreateSessionAsync_CreatesActiveSession_AndSeedsTruthMap()
    {
        var sessionRepository = Substitute.For<ISessionRepository>();
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        var orchestrator = new Orchestrator();
        var sut = new SessionService(sessionRepository, truthMapRepository, orchestrator);

        var session = await sut.CreateSessionAsync(
            idea: "A platform to stress-test startup ideas.",
            mode: SessionMode.Deep,
            frictionLevel: 55,
            CancellationToken.None);

        session.Status.Should().Be(SessionStatus.Active);
        session.Phase.Should().Be(SessionPhase.Clarification);
        session.Mode.Should().Be(SessionMode.Deep);
        session.FrictionLevel.Should().Be(55);

        await sessionRepository.Received(1).CreateAsync(session, Arg.Any<CancellationToken>());
        await truthMapRepository.Received(1).CreateAsync(
            Arg.Is<TruthMapState>(map =>
                map.SessionId == session.SessionId
                && map.CoreIdea == "A platform to stress-test startup ideas."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateSessionAsync_Throws_ForTooShortIdea()
    {
        var sessionRepository = Substitute.For<ISessionRepository>();
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        var orchestrator = new Orchestrator();
        var sut = new SessionService(sessionRepository, truthMapRepository, orchestrator);

        var act = () => sut.CreateSessionAsync("short", SessionMode.Quick, 50, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task StartSessionAsync_TransitionsFromClarificationToDebateRound1()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.Clarification,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = 45,
            RoundPolicy = new RoundPolicy()
        };

        var map = TruthMapState.CreateNew(sessionId);

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(map);

        var orchestrator = new Orchestrator();
        var sut = new SessionService(sessionRepository, truthMapRepository, orchestrator);

        var updated = await sut.StartSessionAsync(sessionId, CancellationToken.None);

        updated.Phase.Should().Be(SessionPhase.DebateRound1);
        updated.RoundNumber.Should().Be(1);
        await sessionRepository.Received(1).UpdateAsync(updated, Arg.Any<CancellationToken>());
    }
}
