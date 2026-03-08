using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Orchestration;
using Agon.Application.Services;
using Agon.Domain.Sessions;
using Agon.Domain.Snapshots;
using FluentAssertions;
using NSubstitute;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Application.Tests.Services;

public class SessionServiceTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TruthMapModel EmptyMap(Guid? id = null) =>
        TruthMapModel.Empty(id ?? SessionId);

    private static SessionState BuildState(
        Guid? sessionId = null,
        SessionPhase phase = SessionPhase.Intake,
        int frictionLevel = 50)
    {
        var id = sessionId ?? SessionId;
        var state = SessionState.Create(id, frictionLevel, false, EmptyMap(id));
        state.Phase = phase;
        return state;
    }

    private static ISessionRepository StubSessionRepo(SessionState? returns = null)
    {
        var repo = Substitute.For<ISessionRepository>();
        if (returns is not null)
        {
            repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<SessionState?>(returns));
        }
        repo.CreateAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<SessionState>(0)));
        repo.UpdateAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return repo;
    }

    private static ITruthMapRepository StubTruthMapRepo(TruthMapModel? map = null)
    {
        var repo = Substitute.For<ITruthMapRepository>();
        var m = map ?? EmptyMap();
        repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TruthMapModel?>(m));
        repo.SaveAsync(Arg.Any<TruthMapModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return repo;
    }

    private static ISnapshotStore StubSnapshotStore()
    {
        var store = Substitute.For<ISnapshotStore>();
        store.SaveAsync(Arg.Any<SessionSnapshot>(), Arg.Any<CancellationToken>())
             .Returns(call => Task.FromResult(call.ArgAt<SessionSnapshot>(0)));
        store.ListBySessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IReadOnlyList<SessionSnapshot>>([]));
        return store;
    }

    private static SessionService BuildService(
        ISessionRepository? sessionRepo = null,
        ITruthMapRepository? truthMapRepo = null,
        ISnapshotStore? snapshotStore = null)
    {
        return new SessionService(
            sessionRepo ?? StubSessionRepo(),
            truthMapRepo ?? StubTruthMapRepo(),
            snapshotStore ?? StubSnapshotStore());
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PersistsSessionAndInitialisesIntakePhase()
    {
        var sessionRepo = StubSessionRepo();
        var svc = BuildService(sessionRepo: sessionRepo);

        var result = await svc.CreateAsync(frictionLevel: 50, researchToolsEnabled: false);

        result.Phase.Should().Be(SessionPhase.Intake);
        result.Status.Should().Be(SessionStatus.Active);
        result.FrictionLevel.Should().Be(50);
        await sessionRepo.Received(1).CreateAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_SeedsEmptyTruthMapInRepository()
    {
        var truthMapRepo = StubTruthMapRepo();
        var svc = BuildService(truthMapRepo: truthMapRepo);

        await svc.CreateAsync(50, false);

        await truthMapRepo.Received(1).SaveAsync(
            Arg.Is<TruthMapModel>(m => m.Version == 0 && m.Round == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_GeneratesUniqueSessionId()
    {
        var svc = BuildService();

        var first = await svc.CreateAsync(50, false);
        var second = await svc.CreateAsync(50, false);

        first.SessionId.Should().NotBe(second.SessionId);
    }

    [Fact]
    public async Task CreateAsync_WithUserContext_PersistsSessionWithUserInfo()
    {
        var userId = Guid.NewGuid();
        var idea = "Build a task management app";
        var sessionRepo = StubSessionRepo();
        var svc = BuildService(sessionRepo: sessionRepo);

        var result = await svc.CreateAsync(userId, idea, frictionLevel: 75);

        result.Phase.Should().Be(SessionPhase.Intake);
        result.Status.Should().Be(SessionStatus.Active);
        result.FrictionLevel.Should().Be(75);
        await sessionRepo.Received(1).CreateAsync(
            Arg.Is<SessionState>(s => s.UserId == userId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithUserContext_SeedsEmptyTruthMap()
    {
        var truthMapRepo = StubTruthMapRepo();
        var svc = BuildService(truthMapRepo: truthMapRepo);

        await svc.CreateAsync(Guid.NewGuid(), "Test idea", 50);

        await truthMapRepo.Received(1).SaveAsync(
            Arg.Is<TruthMapModel>(m => m.Version == 0 && m.Round == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithUserContext_GeneratesUniqueSessionId()
    {
        var userId = Guid.NewGuid();
        var svc = BuildService();

        var first = await svc.CreateAsync(userId, "Idea 1", 50);
        var second = await svc.CreateAsync(userId, "Idea 2", 50);

        first.SessionId.Should().NotBe(second.SessionId);
    }

    // ── GetAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsSessionStateFromRepository()
    {
        var expected = BuildState(phase: SessionPhase.Clarification);
        var svc = BuildService(sessionRepo: StubSessionRepo(expected));

        var result = await svc.GetAsync(SessionId);

        result.Should().NotBeNull();
        result!.Phase.Should().Be(SessionPhase.Clarification);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenSessionNotFound()
    {
        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SessionState?>(null));

        var svc = BuildService(sessionRepo: repo);
        var result = await svc.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── StartClarificationAsync ───────────────────────────────────────────────

    [Fact]
    public async Task StartClarificationAsync_TransitionsFromIntakeToClarification()
    {
        var state = BuildState(phase: SessionPhase.Intake);
        var sessionRepo = StubSessionRepo(state);
        var svc = BuildService(sessionRepo: sessionRepo);

        await svc.StartClarificationAsync(state.SessionId);

        state.Phase.Should().Be(SessionPhase.Clarification);
        await sessionRepo.Received(1).UpdateAsync(
            Arg.Is<SessionState>(s => s.Phase == SessionPhase.Clarification),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartClarificationAsync_ThrowsWhenSessionNotFound()
    {
        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SessionState?>(null));

        var svc = BuildService(sessionRepo: repo);

        var act = async () => await svc.StartClarificationAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task StartClarificationAsync_ThrowsWhenNotInIntakePhase()
    {
        var state = BuildState(phase: SessionPhase.Clarification); // Already in Clarification
        var sessionRepo = StubSessionRepo(state);
        var svc = BuildService(sessionRepo: sessionRepo);

        var act = async () => await svc.StartClarificationAsync(state.SessionId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot start clarification from phase*");
    }

    [Fact]
    public async Task StartClarificationAsync_BroadcastsPhaseTransitionEvent()
    {
        var state = BuildState(phase: SessionPhase.Intake);
        var sessionRepo = StubSessionRepo(state);
        var broadcaster = Substitute.For<IEventBroadcaster>();
        var svcWithBroadcaster = new SessionService(
            sessionRepo,
            StubTruthMapRepo(),
            StubSnapshotStore(),
            broadcaster);

        await svcWithBroadcaster.StartClarificationAsync(state.SessionId);

        await broadcaster.Received(1).SendRoundProgressAsync(
            state.SessionId,
            nameof(SessionPhase.Clarification),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartClarificationAsync_DoesNotBroadcastWhenBroadcasterIsNull()
    {
        var state = BuildState(phase: SessionPhase.Intake);
        var sessionRepo = StubSessionRepo(state);
        var svc = BuildService(sessionRepo: sessionRepo); // No broadcaster

        var act = async () => await svc.StartClarificationAsync(state.SessionId);

        await act.Should().NotThrowAsync();
    }

    // ── AdvancePhaseAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task AdvancePhaseAsync_UpdatesPhaseAndPersists()
    {
        var state = BuildState(phase: SessionPhase.Clarification);
        var sessionRepo = StubSessionRepo(state);
        var svc = BuildService(sessionRepo: sessionRepo);

        await svc.AdvancePhaseAsync(state, SessionPhase.AnalysisRound);

        state.Phase.Should().Be(SessionPhase.AnalysisRound);
        await sessionRepo.Received(1).UpdateAsync(state, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvancePhaseAsync_BroadcastsRoundProgressEvent()
    {
        var state = BuildState(phase: SessionPhase.Clarification);
        var broadcaster = Substitute.For<IEventBroadcaster>();
        var svcWithBroadcaster = new SessionService(
            StubSessionRepo(state),
            StubTruthMapRepo(),
            StubSnapshotStore(),
            broadcaster);

        await svcWithBroadcaster.AdvancePhaseAsync(state, SessionPhase.AnalysisRound);

        await broadcaster.Received(1).SendRoundProgressAsync(
            state.SessionId,
            nameof(SessionPhase.AnalysisRound),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // ── RecordRoundSnapshotAsync ──────────────────────────────────────────────

    [Fact]
    public async Task RecordRoundSnapshotAsync_SavesSnapshotWithCorrectRound()
    {
        var state = BuildState(phase: SessionPhase.AnalysisRound);
        state.CurrentRound = 2;
        var snapshotStore = StubSnapshotStore();
        var svc = BuildService(snapshotStore: snapshotStore);

        await svc.RecordRoundSnapshotAsync(state);

        await snapshotStore.Received(1).SaveAsync(
            Arg.Is<SessionSnapshot>(s => s.Round == 2 && s.SessionId == state.SessionId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordRoundSnapshotAsync_SnapshotContainsCurrentTruthMap()
    {
        var map = EmptyMap() with { CoreIdea = "Test idea", Version = 5 };
        var state = BuildState();
        state.TruthMap = map;

        SessionSnapshot? captured = null;
        var snapshotStore = Substitute.For<ISnapshotStore>();
        snapshotStore.SaveAsync(Arg.Do<SessionSnapshot>(s => captured = s), Arg.Any<CancellationToken>())
                     .Returns(call => Task.FromResult(call.ArgAt<SessionSnapshot>(0)));

        var svc = BuildService(snapshotStore: snapshotStore);
        await svc.RecordRoundSnapshotAsync(state);

        captured.Should().NotBeNull();
        captured!.TruthMap.CoreIdea.Should().Be("Test idea");
        captured.TruthMap.Version.Should().Be(5);
    }

    [Fact]
    public async Task RecordRoundSnapshotAsync_SnapshotHasValidIntegrityHash()
    {
        var state = BuildState();

        SessionSnapshot? captured = null;
        var snapshotStore = Substitute.For<ISnapshotStore>();
        snapshotStore.SaveAsync(Arg.Do<SessionSnapshot>(s => captured = s), Arg.Any<CancellationToken>())
                     .Returns(call => Task.FromResult(call.ArgAt<SessionSnapshot>(0)));

        var svc = BuildService(snapshotStore: snapshotStore);
        await svc.RecordRoundSnapshotAsync(state);

        captured!.IsIntact().Should().BeTrue();
    }

    // ── ListSnapshotsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ListSnapshotsAsync_DelegatesToSnapshotStore()
    {
        var snap1 = SessionSnapshot.Create(EmptyMap(), 1);
        var snap2 = SessionSnapshot.Create(EmptyMap(), 2);

        var snapshotStore = Substitute.For<ISnapshotStore>();
        snapshotStore.ListBySessionAsync(SessionId, Arg.Any<CancellationToken>())
                     .Returns(Task.FromResult<IReadOnlyList<SessionSnapshot>>([snap1, snap2]));

        var svc = BuildService(snapshotStore: snapshotStore);

        var result = await svc.ListSnapshotsAsync(SessionId);

        result.Should().HaveCount(2);
    }

    // ── StartClarificationAsync (Orchestrator Integration) ────────────────────

    [Fact]
    public async Task StartClarificationAsync_Should_CallOrchestratorRunModeratorAsync()
    {
        // Arrange
        var state = BuildState(phase: SessionPhase.Intake);
        var sessionRepo = StubSessionRepo(state);
        var orchestrator = Substitute.For<IOrchestrator>();
        var svc = BuildServiceWithOrchestrator(sessionRepo, orchestrator: orchestrator);

        // Act
        await svc.StartClarificationAsync(state.SessionId);

        // Assert
        await orchestrator.Received(1).RunModeratorAsync(
            Arg.Is<SessionState>(s => s.SessionId == state.SessionId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartClarificationAsync_Should_TransitionToAnalysisPhaseAfterModeratorReturnsReady()
    {
        // Arrange
        var state = BuildState(phase: SessionPhase.Intake);
        var sessionRepo = StubSessionRepo(state);
        var orchestrator = Substitute.For<IOrchestrator>();
        
        // Configure orchestrator to simulate READY signal by transitioning phase
        orchestrator
            .RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var s = call.ArgAt<SessionState>(0);
                s.Phase = SessionPhase.AnalysisRound;
                return Task.CompletedTask;
            });

        var svc = BuildServiceWithOrchestrator(sessionRepo, orchestrator: orchestrator);

        // Act
        await svc.StartClarificationAsync(state.SessionId);

        // Assert
        state.Phase.Should().Be(SessionPhase.AnalysisRound);
    }

    [Fact]
    public async Task StartClarificationAsync_Should_UpdateSessionAfterOrchestratorCall()
    {
        // Arrange
        var state = BuildState(phase: SessionPhase.Intake);
        var sessionRepo = StubSessionRepo(state);
        var orchestrator = Substitute.For<IOrchestrator>();
        var svc = BuildServiceWithOrchestrator(sessionRepo, orchestrator: orchestrator);

        // Act
        await svc.StartClarificationAsync(state.SessionId);

        // Assert
        await sessionRepo.Received().UpdateAsync(
            Arg.Is<SessionState>(s => s.SessionId == state.SessionId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartClarificationAsync_Should_BroadcastPhaseTransitionAfterOrchestratorCall()
    {
        // Arrange
        var state = BuildState(phase: SessionPhase.Intake);
        var sessionRepo = StubSessionRepo(state);
        var orchestrator = Substitute.For<IOrchestrator>();
        var broadcaster = Substitute.For<IEventBroadcaster>();
        var svc = BuildServiceWithOrchestrator(sessionRepo, orchestrator, broadcaster);

        // Act
        await svc.StartClarificationAsync(state.SessionId);

        // Assert
        await broadcaster.Received(1).SendRoundProgressAsync(
            state.SessionId,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // ── SubmitMessageAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitMessageAsync_Should_AddMessageToSessionState()
    {
        // Arrange
        var state = BuildState(phase: SessionPhase.Clarification);
        state.ClarificationRoundCount = 1;
        var sessionRepo = StubSessionRepo(returns: state);
        var svc = BuildService(sessionRepo: sessionRepo);

        // Act
        await svc.SubmitMessageAsync(state.SessionId, "Target customers are small businesses", CancellationToken.None);

        // Assert
        state.UserMessages.Should().HaveCount(1);
        state.UserMessages[0].Content.Should().Be("Target customers are small businesses");
        state.UserMessages[0].ClarificationRound.Should().Be(1);
        await sessionRepo.Received(1).UpdateAsync(
            Arg.Is<SessionState>(s => s.UserMessages.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitMessageAsync_Should_CallOrchestrator()
    {
        // Arrange
        var state = BuildState(phase: SessionPhase.Clarification);
        var sessionRepo = StubSessionRepo(returns: state);
        var orchestrator = Substitute.For<IOrchestrator>();
        var svc = BuildServiceWithOrchestrator(sessionRepo: sessionRepo, orchestrator: orchestrator);

        // Act
        await svc.SubmitMessageAsync(state.SessionId, "Test message", CancellationToken.None);

        // Assert
        await orchestrator.Received(1).RunModeratorAsync(
            Arg.Is<SessionState>(s => s.UserMessages.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitMessageAsync_Should_ThrowIfSessionNotFound()
    {
        // Arrange
        var sessionRepo = StubSessionRepo(returns: null);
        var svc = BuildService(sessionRepo: sessionRepo);
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SubmitMessageAsync(nonExistentId, "Test", CancellationToken.None));
    }

    [Fact]
    public async Task SubmitMessageAsync_Should_ThrowIfNotInClarificationPhase()
    {
        // Arrange
        var state = BuildState(phase: SessionPhase.AnalysisRound);
        var sessionRepo = StubSessionRepo(returns: state);
        var svc = BuildService(sessionRepo: sessionRepo);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SubmitMessageAsync(state.SessionId, "Test", CancellationToken.None));
    }

    // ── Helper for Orchestrator Injection ─────────────────────────────────────

    private static SessionService BuildServiceWithOrchestrator(
        ISessionRepository? sessionRepo = null,
        IOrchestrator? orchestrator = null,
        IEventBroadcaster? broadcaster = null,
        ITruthMapRepository? truthMapRepo = null,
        ISnapshotStore? snapshotStore = null)
    {
        return new SessionService(
            sessionRepo ?? StubSessionRepo(),
            truthMapRepo ?? StubTruthMapRepo(),
            snapshotStore ?? StubSnapshotStore(),
            broadcaster,
            orchestrator ?? Substitute.For<IOrchestrator>());
    }
}
