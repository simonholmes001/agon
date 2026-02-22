using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Sessions;
using Agon.Application.Services;
using Agon.Domain.Agents;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Agon.Application.Tests.Services;

public class SessionServiceTests
{
    [Fact]
    public async Task CreateSessionAsync_CreatesActiveSession_AndSeedsTruthMap()
    {
        var sessionRepository = Substitute.For<ISessionRepository>();
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [],
            eventBroadcaster,
            logger);

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
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [],
            eventBroadcaster,
            logger);

        var act = () => sut.CreateSessionAsync("short", SessionMode.Quick, 50, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateSessionAsync_Throws_ForOutOfRangeFrictionLevel()
    {
        var sessionRepository = Substitute.For<ISessionRepository>();
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [],
            eventBroadcaster,
            logger);

        var actLow = () => sut.CreateSessionAsync("A valid enough idea text.", SessionMode.Deep, -1, CancellationToken.None);
        var actHigh = () => sut.CreateSessionAsync("A valid enough idea text.", SessionMode.Deep, 101, CancellationToken.None);

        await actLow.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await actHigh.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task StartSessionAsync_TransitionsFromClarificationToPostDelivery()
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
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();

        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [],
            eventBroadcaster,
            logger);

        var updated = await sut.StartSessionAsync(sessionId, CancellationToken.None);

        updated.Phase.Should().Be(SessionPhase.PostDelivery);
        updated.Status.Should().Be(SessionStatus.CompleteWithGaps);
        updated.RoundNumber.Should().Be(1);
        await sessionRepository.Received(1).UpdateAsync(updated, Arg.Any<CancellationToken>());
        await eventBroadcaster.Received(1)
            .RoundProgressAsync(sessionId, SessionPhase.DebateRound1, Arg.Any<CancellationToken>());
        await transcriptRepository.Received(1).AppendAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.Type == TranscriptMessageType.System
                && message.AgentId == null
                && message.Content.Contains("Round 1 started", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostUserMessageAsync_InClarification_RoutesToSocraticClarifier()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.Clarification,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy(),
            RoundNumber = 0
        };
        var map = TruthMapState.CreateNew(sessionId);
        map.CoreIdea = "Core idea";

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(map);
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var clarifier = new ContextCapturingAgent(AgentId.SocraticClarifier, "Clarifier reply.");
        var product = new ContextCapturingAgent(AgentId.ProductStrategist, "Product reply.");
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [clarifier, product],
            eventBroadcaster,
            logger);

        var result = await sut.PostUserMessageAsync(
            sessionId,
            "Here are my answers to your questions.",
            CancellationToken.None,
            "corr-msg-1");

        result.Phase.Should().Be("Clarification");
        result.RoutedAgentId.Should().Be(AgentId.SocraticClarifier);
        result.Reply.Should().Be("Clarifier reply.");
        clarifier.LastContext.Should().NotBeNull();
        clarifier.LastContext!.CorrelationId.Should().Be("corr-msg-1");
        await transcriptRepository.Received(2).AppendAsync(
            sessionId,
            Arg.Any<TranscriptMessage>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostUserMessageAsync_InPostDelivery_RoutesByKeyword()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.PostDelivery,
            Status = SessionStatus.CompleteWithGaps,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy(),
            RoundNumber = 1
        };
        var map = TruthMapState.CreateNew(sessionId);
        map.CoreIdea = "Core idea";

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(map);
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var technical = new ContextCapturingAgent(AgentId.TechnicalArchitect, "Technical reply.");
        var contrarian = new ContextCapturingAgent(AgentId.Contrarian, "Contrarian reply.");
        var product = new ContextCapturingAgent(AgentId.ProductStrategist, "Product reply.");
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [technical, contrarian, product],
            eventBroadcaster,
            logger);

        var result = await sut.PostUserMessageAsync(
            sessionId,
            "Can you explain architecture scalability trade-offs?",
            CancellationToken.None);

        result.Phase.Should().Be("PostDelivery");
        result.RoutedAgentId.Should().Be(AgentId.TechnicalArchitect);
        result.Reply.Should().Be("Technical reply.");
        technical.LastContext.Should().NotBeNull();
        contrarian.LastContext.Should().NotBeNull();
    }

    [Fact]
    public async Task PostUserMessageAsync_StreamsCompactAgentMessages()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.PostDelivery,
            Status = SessionStatus.CompleteWithGaps,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy(),
            RoundNumber = 1
        };
        var map = TruthMapState.CreateNew(sessionId);
        map.CoreIdea = "Core idea";

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(map);
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        const string agentMessage = "Concise response that still spans length.";
        var technical = new ContextCapturingAgent(AgentId.TechnicalArchitect, agentMessage);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [technical],
            eventBroadcaster,
            logger);

        await sut.PostUserMessageAsync(
            sessionId,
            "Need a technical stack recommendation.",
            CancellationToken.None);

        await eventBroadcaster.Received().TranscriptMessageAppendedAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.AgentId == "technical-architect"
                && message.IsStreaming),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostUserMessageAsync_Throws_ForBlankMessage()
    {
        var sessionId = Guid.NewGuid();
        var sessionRepository = Substitute.For<ISessionRepository>();
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [],
            eventBroadcaster,
            logger);

        var act = () => sut.PostUserMessageAsync(sessionId, "   ", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PostUserMessageAsync_Throws_ForUnsupportedPhase()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.DebateRound1,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy(),
            RoundNumber = 1
        };
        var map = TruthMapState.CreateNew(sessionId);
        map.CoreIdea = "Core idea";

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(map);
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [],
            eventBroadcaster,
            logger);

        var act = () => sut.PostUserMessageAsync(sessionId, "Please continue", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartSessionAsync_Throws_WhenSessionIsMissing()
    {
        var sessionId = Guid.NewGuid();
        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns((SessionState?)null);
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [],
            eventBroadcaster,
            logger);

        var act = () => sut.StartSessionAsync(sessionId, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task StartSessionAsync_Throws_WhenTruthMapIsMissing()
    {
        var sessionId = Guid.NewGuid();
        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.Clarification,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy()
        });
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns((TruthMapState?)null);
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [],
            eventBroadcaster,
            logger);

        var act = () => sut.StartSessionAsync(sessionId, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task StartSessionAsync_RunsCouncilRound_AndStoresAgentMessages()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.Clarification,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy()
        };
        var map = TruthMapState.CreateNew(sessionId);

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(map);

        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        const string agentMessage =
            "A concise product strategy message with enough detail to demonstrate incremental streaming updates over several chunks and ensure multiple segments are emitted.";
        var councilAgents = new ICouncilAgent[]
        {
            new StaticAgent("product_strategist", agentMessage)
        };
        var logger = Substitute.For<ILogger<SessionService>>();

        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            councilAgents,
            eventBroadcaster,
            logger);

        await sut.StartSessionAsync(sessionId, CancellationToken.None);

        await transcriptRepository.Received(4).AppendAsync(
            sessionId,
            Arg.Any<TranscriptMessage>(),
            Arg.Any<CancellationToken>());
        await transcriptRepository.Received(1).AppendAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.Type == TranscriptMessageType.System
                && message.AgentId == null
                && message.Content.Contains("Round 1 started", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await transcriptRepository.Received(1).AppendAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.AgentId == "product-strategist"
                && message.Content == agentMessage
                && message.Round == 1),
            Arg.Any<CancellationToken>());
        await transcriptRepository.Received(1).AppendAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.AgentId == "synthesis-validation"
                && message.Type == TranscriptMessageType.Agent
                && message.Content.Contains("Moderator summary", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("Agent summaries", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await transcriptRepository.Received(1).AppendAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.Type == TranscriptMessageType.System
                && message.Content.Contains("Round 1 complete", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartSessionAsync_DoesNotDispatchCouncil_WhenPhaseIsNotClarification()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.DebateRound1,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy(),
            RoundNumber = 1
        };

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(TruthMapState.CreateNew(sessionId));
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [new StaticAgent("product_strategist", "message")],
            eventBroadcaster,
            logger);

        var updated = await sut.StartSessionAsync(sessionId, CancellationToken.None);

        updated.Phase.Should().Be(SessionPhase.DebateRound1);
        await eventBroadcaster.DidNotReceive().RoundProgressAsync(
            Arg.Any<Guid>(),
            Arg.Any<SessionPhase>(),
            Arg.Any<CancellationToken>());
        await transcriptRepository.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<TranscriptMessage>(),
            Arg.Any<CancellationToken>());
        await sessionRepository.Received(1).UpdateAsync(updated, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartSessionAsync_BroadcastsTranscriptMessages_WhenRoundMessagesAreStored()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.Clarification,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy()
        };
        var map = TruthMapState.CreateNew(sessionId);

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(map);

        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        const string agentMessage =
            "A concise product strategy message with enough detail to demonstrate incremental streaming updates over several chunks and ensure multiple segments are emitted.";
        var councilAgents = new ICouncilAgent[]
        {
            new StaticAgent("product_strategist", agentMessage)
        };
        var logger = Substitute.For<ILogger<SessionService>>();

        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            councilAgents,
            eventBroadcaster,
            logger);

        await sut.StartSessionAsync(sessionId, CancellationToken.None);

        await eventBroadcaster.Received(1).TranscriptMessageAppendedAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.Type == TranscriptMessageType.System
                && message.Content.Contains("Round 1 started", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());

        await eventBroadcaster.Received().TranscriptMessageAppendedAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.AgentId == "product-strategist"
                && message.Content == agentMessage),
            Arg.Any<CancellationToken>());

        await eventBroadcaster.Received().TranscriptMessageAppendedAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.AgentId == "product-strategist"
                && message.IsStreaming),
            Arg.Any<CancellationToken>());

        await eventBroadcaster.Received().TranscriptMessageAppendedAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.AgentId == "synthesis-validation"
                && message.Type == TranscriptMessageType.Agent),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartSessionAsync_AppendsSystemFailureMessage_WhenAgentFails()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.Clarification,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy()
        };
        var map = TruthMapState.CreateNew(sessionId);

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(map);

        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var councilAgents = new ICouncilAgent[]
        {
            new FailingAgent("product_strategist", "missing api key")
        };
        var logger = Substitute.For<ILogger<SessionService>>();

        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            councilAgents,
            eventBroadcaster,
            logger);

        await sut.StartSessionAsync(sessionId, CancellationToken.None);

        await transcriptRepository.Received(1).AppendAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.Type == TranscriptMessageType.System
                && message.Content.Contains("product-strategist")
                && message.Content.Contains("missing api key")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartSessionAsync_PropagatesCorrelationId_ToAgentContext()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.Clarification,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy()
        };
        var map = TruthMapState.CreateNew(sessionId);

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(map);

        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var capturingAgent = new ContextCapturingAgent("product_strategist", "message");
        var logger = Substitute.For<ILogger<SessionService>>();

        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [capturingAgent],
            eventBroadcaster,
            logger);

        await sut.StartSessionAsync(sessionId, CancellationToken.None, "corr-start-123");

        capturingAgent.LastContext.Should().NotBeNull();
        capturingAgent.LastContext!.CorrelationId.Should().Be("corr-start-123");
    }

    [Fact]
    public async Task StartSessionAsync_UsesDefaultCorrelationId_WhenCorrelationIdIsBlank()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.Clarification,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy()
        };
        var map = TruthMapState.CreateNew(sessionId);

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(map);
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var capturingAgent = new ContextCapturingAgent("product_strategist", "message");
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [capturingAgent],
            eventBroadcaster,
            logger);

        await sut.StartSessionAsync(sessionId, CancellationToken.None, "   ");

        capturingAgent.LastContext.Should().NotBeNull();
        capturingAgent.LastContext!.CorrelationId.Should().Be("n/a");
    }

    [Fact]
    public async Task StartSessionAsync_TrimsLongAgentErrors_InSystemFailureMessage()
    {
        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Phase = SessionPhase.Clarification,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = 50,
            RoundPolicy = new RoundPolicy()
        };
        var map = TruthMapState.CreateNew(sessionId);
        var longError = new string('x', 260);
        var trimmedError = longError[..180];

        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        truthMapRepository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(map);
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [new FailingAgent("product_strategist", longError)],
            eventBroadcaster,
            logger);

        await sut.StartSessionAsync(sessionId, CancellationToken.None);

        await transcriptRepository.Received().AppendAsync(
            sessionId,
            Arg.Is<TranscriptMessage>(message =>
                message.Type == TranscriptMessageType.System
                && message.Content.Contains("product-strategist")
                && message.Content.Contains(trimmedError)
                && !message.Content.Contains(longError)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTranscriptAsync_ReturnsMessagesFromRepository()
    {
        var sessionId = Guid.NewGuid();
        var expected = new[]
        {
            new TranscriptMessage
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                Type = TranscriptMessageType.Agent,
                AgentId = "socratic-clarifier",
                Content = "Kickoff transcript",
                Round = 1,
                IsStreaming = false,
                CreatedAtUtc = DateTimeOffset.UtcNow
            }
        };

        var sessionRepository = Substitute.For<ISessionRepository>();
        var truthMapRepository = Substitute.For<ITruthMapRepository>();
        var transcriptRepository = Substitute.For<ITranscriptRepository>();
        transcriptRepository
            .GetBySessionAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(expected);
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var orchestrator = new Orchestrator();
        var runnerLogger = Substitute.For<ILogger<AgentRunner>>();
        var agentRunner = new AgentRunner(truthMapRepository, eventBroadcaster, runnerLogger);
        var logger = Substitute.For<ILogger<SessionService>>();
        var sut = new SessionService(
            sessionRepository,
            truthMapRepository,
            transcriptRepository,
            orchestrator,
            agentRunner,
            [],
            eventBroadcaster,
            logger);

        var transcript = await sut.GetTranscriptAsync(sessionId, CancellationToken.None);

        transcript.Should().BeEquivalentTo(expected);
    }

    private sealed class StaticAgent(string agentId, string message) : ICouncilAgent
    {
        public string AgentId { get; } = agentId;
        public string ModelProvider => "test";

        public Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentResponse
            {
                Message = message,
                Patch = null,
                RawOutput = message
            });
        }

        public async IAsyncEnumerable<string> RunStreamingAsync(
            AgentContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return message;
        }
    }

    private sealed class FailingAgent(string agentId, string error) : ICouncilAgent
    {
        public string AgentId { get; } = agentId;
        public string ModelProvider => "test";

        public Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(error);
        }

        public async IAsyncEnumerable<string> RunStreamingAsync(
            AgentContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException(error);
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class ContextCapturingAgent(string agentId, string message) : ICouncilAgent
    {
        public string AgentId { get; } = agentId;
        public string ModelProvider => "test";
        public AgentContext? LastContext { get; private set; }

        public Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            LastContext = context;
            return Task.FromResult(new AgentResponse
            {
                Message = message,
                RawOutput = message
            });
        }

        public async IAsyncEnumerable<string> RunStreamingAsync(
            AgentContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return message;
        }
    }
}
