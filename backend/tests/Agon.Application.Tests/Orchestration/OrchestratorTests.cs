using Agon.Application.Models;
using Agon.Application.Orchestration;
using Agon.Application.Services;
using Agon.Domain.Agents;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;
using NSubstitute;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Application.Tests.Orchestration;

public class OrchestratorTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TruthMapModel EmptyMap() => TruthMapModel.Empty(SessionId);

    private static SessionState BuildState(
        SessionPhase phase = SessionPhase.Intake,
        int frictionLevel = 50,
        int tokensUsed = 0)
    {
        var state = SessionState.Create(SessionId, frictionLevel, false, EmptyMap());
        state.Phase = phase;
        state.TokensUsed = tokensUsed;
        return state;
    }

    private static RoundPolicy DefaultPolicy() => RoundPolicy.Default();

    private static RoundPolicy ExhaustedBudgetPolicy() =>
        new() { MaxSessionBudgetTokens = 100 };

    private static IAgentRunner StubRunner(
        AgentResponse? synthesisResponse = null,
        IReadOnlyList<AgentResponse>? roundResponses = null)
    {
        var runner = Substitute.For<IAgentRunner>();
        var responses = roundResponses ?? [
            new AgentResponse(AgentId.ClaudeAgent, "Claude msg", null, 100, false, null),
            new AgentResponse(AgentId.GeminiAgent, "Gemini msg", null, 100, false, null),
            new AgentResponse(AgentId.GptAgent, "GPT msg", null, 100, false, null)
        ];

        runner.RunAnalysisRoundAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult((IReadOnlyList<AgentResponse>)responses));

        runner.RunCritiqueRoundAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult((IReadOnlyList<AgentResponse>)responses));

        runner.RunTargetedLoopAsync(
                Arg.Any<SessionState>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult((IReadOnlyList<AgentResponse>)responses));

        var synthResponse = synthesisResponse ??
            new AgentResponse(AgentId.Synthesizer, "Synthesis msg", null, 200, false, null);
        runner.RunSynthesisAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(synthResponse));

        // Mock RunModeratorAsync - will be configured per-test as needed
        runner.RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(
                  new AgentResponse(AgentId.Moderator, "Moderator response", null, 100, false, null)));

        return runner;
    }

    private static ISessionService StubSessionService()
    {
        var svc = Substitute.For<ISessionService>();
        svc.AdvancePhaseAsync(Arg.Any<SessionState>(), Arg.Any<SessionPhase>(), Arg.Any<CancellationToken>())
           .Returns(Task.CompletedTask);
        svc.RecordRoundSnapshotAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
           .Returns(Task.CompletedTask);
        return svc;
    }

    private static Orchestrator BuildOrchestrator(
        IAgentRunner? runner = null,
        ISessionService? sessionService = null,
        RoundPolicy? policy = null)
    {
        return new Orchestrator(
            runner ?? StubRunner(),
            sessionService ?? StubSessionService(),
            policy ?? DefaultPolicy());
    }

    // ── StartSessionAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task StartSessionAsync_AdvancesToClarification()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.Intake);

        await orchestrator.StartSessionAsync(state, CancellationToken.None);

        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.Clarification, Arg.Any<CancellationToken>());
    }

    // ── SignalClarificationCompleteAsync ──────────────────────────────────────

    [Fact]
    public async Task SignalClarificationCompleteAsync_StoresBriefAndAdvancesToAnalysis()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.Clarification);
        var brief = new DebateBrief(
            "A great idea",
            new BriefConstraints("$10k", "3 months", [], []),
            ["Revenue"],
            "Developer",
            []);

        await orchestrator.SignalClarificationCompleteAsync(state, brief, CancellationToken.None);

        state.DebateBrief.Should().Be(brief);
        state.ClarificationIncomplete.Should().BeFalse();
        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.AnalysisRound, Arg.Any<CancellationToken>());
    }

    // ── SignalClarificationTimedOutAsync ──────────────────────────────────────

    [Fact]
    public async Task SignalClarificationTimedOutAsync_FlagsClarificationIncompleteAndAdvances()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.Clarification);
        var partialBrief = new DebateBrief(
            "Incomplete idea",
            new BriefConstraints("unknown", "unknown", [], []),
            [],
            "",
            []);

        await orchestrator.SignalClarificationTimedOutAsync(state, partialBrief, CancellationToken.None);

        state.ClarificationIncomplete.Should().BeTrue();
        state.DebateBrief.Should().Be(partialBrief);
        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.AnalysisRound, Arg.Any<CancellationToken>());
    }

    // ── RunAnalysisRoundAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RunAnalysisRoundAsync_DelegatesDispatchToAgentRunner()
    {
        var runner = StubRunner();
        var orchestrator = BuildOrchestrator(runner: runner);
        var state = BuildState(SessionPhase.AnalysisRound);

        await orchestrator.RunAnalysisRoundAsync(state, CancellationToken.None);

        await runner.Received(1).RunAnalysisRoundAsync(state, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_CachesAgentMessagesForCritiquePhase()
    {
        var responses = new List<AgentResponse>
        {
            new(AgentId.ClaudeAgent, "Claude's analysis", null, 100, false, null),
            new(AgentId.GeminiAgent, "Gemini's analysis", null, 100, false, null),
        };
        var runner = StubRunner(roundResponses: responses);
        var orchestrator = BuildOrchestrator(runner: runner);
        var state = BuildState(SessionPhase.AnalysisRound);

        await orchestrator.RunAnalysisRoundAsync(state, CancellationToken.None);

        state.LastRoundMessages[AgentId.ClaudeAgent].Should().Be("Claude's analysis");
        state.LastRoundMessages[AgentId.GeminiAgent].Should().Be("Gemini's analysis");
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_DoesNotCacheTimedOutAgentMessages()
    {
        var responses = new List<AgentResponse>
        {
            new(AgentId.ClaudeAgent, "Claude's analysis", null, 100, false, null),
            AgentResponse.CreateTimedOut(AgentId.GptAgent),
        };
        var runner = StubRunner(roundResponses: responses);
        var orchestrator = BuildOrchestrator(runner: runner);
        var state = BuildState(SessionPhase.AnalysisRound);

        await orchestrator.RunAnalysisRoundAsync(state, CancellationToken.None);

        state.LastRoundMessages.Should().ContainKey(AgentId.ClaudeAgent);
        state.LastRoundMessages.Should().NotContainKey(AgentId.GptAgent);
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_RecordsRoundSnapshot()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.AnalysisRound);

        await orchestrator.RunAnalysisRoundAsync(state, CancellationToken.None);

        await sessionService.Received(1).RecordRoundSnapshotAsync(state, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_AdvancesToCritique_WhenBudgetNotExhausted()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.AnalysisRound);

        await orchestrator.RunAnalysisRoundAsync(state, CancellationToken.None);

        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.Critique, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_TerminatesWithGaps_WhenBudgetExhausted()
    {
        var sessionService = StubSessionService();
        // 300 tokens used by the 3 agent responses, budget is 100 — exhausted
        var policy = ExhaustedBudgetPolicy();
        var orchestrator = BuildOrchestrator(sessionService: sessionService, policy: policy);
        var state = BuildState(SessionPhase.AnalysisRound, tokensUsed: 200);

        await orchestrator.RunAnalysisRoundAsync(state, CancellationToken.None);

        state.Status.Should().Be(SessionStatus.CompleteWithGaps);
        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.DeliverWithGaps, Arg.Any<CancellationToken>());
    }

    // ── RunCritiqueRoundAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RunCritiqueRoundAsync_DelegatesDispatchToAgentRunner()
    {
        var runner = StubRunner();
        var orchestrator = BuildOrchestrator(runner: runner);
        var state = BuildState(SessionPhase.Critique);

        await orchestrator.RunCritiqueRoundAsync(state, CancellationToken.None);

        await runner.Received(1).RunCritiqueRoundAsync(state, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCritiqueRoundAsync_RecordsRoundSnapshot()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.Critique);

        await orchestrator.RunCritiqueRoundAsync(state, CancellationToken.None);

        await sessionService.Received(1).RecordRoundSnapshotAsync(state, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCritiqueRoundAsync_AdvancesToSynthesis_WhenBudgetNotExhausted()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.Critique);

        await orchestrator.RunCritiqueRoundAsync(state, CancellationToken.None);

        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.Synthesis, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCritiqueRoundAsync_TerminatesWithGaps_WhenBudgetExhausted()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(
            sessionService: sessionService,
            policy: ExhaustedBudgetPolicy());
        var state = BuildState(SessionPhase.Critique, tokensUsed: 200);

        await orchestrator.RunCritiqueRoundAsync(state, CancellationToken.None);

        state.Status.Should().Be(SessionStatus.CompleteWithGaps);
        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.DeliverWithGaps, Arg.Any<CancellationToken>());
    }

    // ── RunSynthesisAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunSynthesisAsync_DelegatesDispatchToAgentRunner()
    {
        var runner = StubRunner();
        var orchestrator = BuildOrchestrator(runner: runner);
        var state = BuildState(SessionPhase.Synthesis);

        await orchestrator.RunSynthesisAsync(state, CancellationToken.None);

        await runner.Received(1).RunSynthesisAsync(state, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSynthesisAsync_AdvancesToDeliver_WhenConverged()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);

        // Build a map with convergence scores above all thresholds
        var convergedMap = EmptyMap() with
        {
            Convergence = new Domain.TruthMap.Entities.Convergence(
                ClaritySpecificity: 0.9f, Feasibility: 0.9f, RiskCoverage: 0.9f,
                AssumptionExplicitness: 0.9f, Coherence: 0.9f, Actionability: 0.9f,
                EvidenceQuality: 0.9f, Overall: 0.9f,
                Threshold: 0.75f, Status: ConvergenceStatus.Converged)
        };
        var state = BuildState(SessionPhase.Synthesis);
        state.TruthMap = convergedMap;

        await orchestrator.RunSynthesisAsync(state, CancellationToken.None);

        state.Status.Should().Be(SessionStatus.Complete);
        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.Deliver, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSynthesisAsync_AdvancesToTargetedLoop_WhenNotConvergedAndLoopBudgetAvailable()
    {
        var sessionService = StubSessionService();
        var policy = new RoundPolicy { MaxTargetedLoops = 2 };
        var orchestrator = BuildOrchestrator(sessionService: sessionService, policy: policy);

        // Map with low convergence scores
        var lowConvergenceMap = EmptyMap() with
        {
            Convergence = new Domain.TruthMap.Entities.Convergence(
                ClaritySpecificity: 0.4f, Feasibility: 0.4f, RiskCoverage: 0.4f,
                AssumptionExplicitness: 0.4f, Coherence: 0.4f, Actionability: 0.4f,
                EvidenceQuality: 0.4f, Overall: 0.4f,
                Threshold: 0.75f, Status: ConvergenceStatus.GapsRemain)
        };
        var state = BuildState(SessionPhase.Synthesis);
        state.TruthMap = lowConvergenceMap;
        state.TargetedLoopCount = 0;

        await orchestrator.RunSynthesisAsync(state, CancellationToken.None);

        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.TargetedLoop, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSynthesisAsync_TerminatesWithGaps_WhenMaxLoopsExhausted()
    {
        var sessionService = StubSessionService();
        var policy = new RoundPolicy { MaxTargetedLoops = 2 };
        var orchestrator = BuildOrchestrator(sessionService: sessionService, policy: policy);

        var lowConvergenceMap = EmptyMap() with
        {
            Convergence = new Domain.TruthMap.Entities.Convergence(
                ClaritySpecificity: 0.4f, Feasibility: 0.4f, RiskCoverage: 0.4f,
                AssumptionExplicitness: 0.4f, Coherence: 0.4f, Actionability: 0.4f,
                EvidenceQuality: 0.4f, Overall: 0.4f,
                Threshold: 0.75f, Status: ConvergenceStatus.GapsRemain)
        };
        var state = BuildState(SessionPhase.Synthesis);
        state.TruthMap = lowConvergenceMap;
        state.TargetedLoopCount = 2; // already at max

        await orchestrator.RunSynthesisAsync(state, CancellationToken.None);

        state.Status.Should().Be(SessionStatus.CompleteWithGaps);
        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.DeliverWithGaps, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSynthesisAsync_TerminatesWithGaps_WhenBudgetExhausted()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(
            sessionService: sessionService,
            policy: ExhaustedBudgetPolicy());
        var state = BuildState(SessionPhase.Synthesis, tokensUsed: 200);

        await orchestrator.RunSynthesisAsync(state, CancellationToken.None);

        state.Status.Should().Be(SessionStatus.CompleteWithGaps);
        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.DeliverWithGaps, Arg.Any<CancellationToken>());
    }

    // ── RunTargetedLoopAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RunTargetedLoopAsync_DelegatesDispatchToAgentRunner()
    {
        var runner = StubRunner();
        var orchestrator = BuildOrchestrator(runner: runner);
        var state = BuildState(SessionPhase.TargetedLoop);

        await orchestrator.RunTargetedLoopAsync(
            state, [AgentId.GptAgent], "Address feasibility gap.", CancellationToken.None);

        await runner.Received(1).RunTargetedLoopAsync(
            state,
            Arg.Is<IReadOnlyList<string>>(ids => ids.Contains(AgentId.GptAgent)),
            "Address feasibility gap.",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunTargetedLoopAsync_IncrementsTargetedLoopCounter()
    {
        var orchestrator = BuildOrchestrator();
        var state = BuildState(SessionPhase.TargetedLoop);
        state.TargetedLoopCount = 1;

        await orchestrator.RunTargetedLoopAsync(
            state, [AgentId.ClaudeAgent], "Fix coherence.", CancellationToken.None);

        state.TargetedLoopCount.Should().Be(2);
    }

    [Fact]
    public async Task RunTargetedLoopAsync_IncrementsRoundCounter()
    {
        var orchestrator = BuildOrchestrator();
        var state = BuildState(SessionPhase.TargetedLoop);
        state.CurrentRound = 3;

        await orchestrator.RunTargetedLoopAsync(
            state, [AgentId.ClaudeAgent], "Fix coherence.", CancellationToken.None);

        state.CurrentRound.Should().Be(4);
    }

    [Fact]
    public async Task RunTargetedLoopAsync_RecordsSnapshot()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.TargetedLoop);

        await orchestrator.RunTargetedLoopAsync(
            state, [AgentId.GptAgent], "Gap directive.", CancellationToken.None);

        await sessionService.Received(1).RecordRoundSnapshotAsync(state, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunTargetedLoopAsync_AdvancesToSynthesisAfterCompletion()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.TargetedLoop);

        await orchestrator.RunTargetedLoopAsync(
            state, [AgentId.GptAgent], "Gap directive.", CancellationToken.None);

        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.Synthesis, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunTargetedLoopAsync_TerminatesWithGaps_WhenBudgetExhausted()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(
            sessionService: sessionService,
            policy: ExhaustedBudgetPolicy());
        var state = BuildState(SessionPhase.TargetedLoop, tokensUsed: 200);

        await orchestrator.RunTargetedLoopAsync(
            state, [AgentId.GptAgent], "Gap directive.", CancellationToken.None);

        state.Status.Should().Be(SessionStatus.CompleteWithGaps);
        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.DeliverWithGaps, Arg.Any<CancellationToken>());
    }

    // ── Clarification Phase Tests ────────────────────────────────────────────

    [Fact]
    public async Task RunModeratorAsync_Should_CallAgentRunner()
    {
        // Arrange
        var runner = Substitute.For<IAgentRunner>();
        var moderatorResponse = new AgentResponse(
            AgentId.Moderator,
            "What is your target audience?",
            null,
            100,
            false,
            null);
        
        runner.RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(moderatorResponse));

        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(
            runner: runner,
            sessionService: sessionService);
        
        var state = BuildState(SessionPhase.Clarification);

        // Act
        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        // Assert
        await runner.Received(1).RunModeratorAsync(
            state, 
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunModeratorAsync_Should_ApplyPatchWhenPresent()
    {
        // Arrange
        var runner = Substitute.For<IAgentRunner>();
        var patch = new Domain.TruthMap.TruthMapPatch(
            new[] {
                new Domain.TruthMap.PatchOperation(
                    Domain.TruthMap.PatchOp.Add,
                    "/constraints",
                    new Constraints("Budget: $50k", "Timeline: 6 months", Array.Empty<string>(), Array.Empty<string>()))
            }.ToList(),
            new Domain.TruthMap.PatchMeta(
                AgentId.Moderator,
                0,
                "Initial brief seeding",
                SessionId));

        var moderatorResponse = new AgentResponse(
            AgentId.Moderator,
            "What is your target audience?",
            patch,
            100,
            false,
            null);
        
        // Mock the AgentRunner to apply the patch to state.TruthMap when called
        runner.RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
              .Returns(callInfo =>
              {
                  var state = callInfo.Arg<SessionState>();
                  // Simulate patch application (increment version)
                  var updated = state.TruthMap with { Version = state.TruthMap.Version + 1 };
                  state.TruthMap = updated;
                  return Task.FromResult(moderatorResponse);
              });

        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(
            runner: runner,
            sessionService: sessionService);
        
        var state = BuildState(SessionPhase.Clarification);
        var initialVersion = state.TruthMap.Version;

        // Act
        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        // Assert
        state.TruthMap.Version.Should().Be(initialVersion + 1);
        await runner.Received(1).RunModeratorAsync(
            state,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunModeratorAsync_WithReadySignal_Should_TransitionToAnalysisRound()
    {
        // Arrange
        var runner = Substitute.For<IAgentRunner>();
        var patch = new Domain.TruthMap.TruthMapPatch(
            new[] {
                new Domain.TruthMap.PatchOperation(
                    Domain.TruthMap.PatchOp.Add,
                    "/constraints",
                    new Constraints("Budget: $50k", "Timeline: 6 months", Array.Empty<string>(), Array.Empty<string>()))
            }.ToList(),
            new Domain.TruthMap.PatchMeta(
                AgentId.Moderator,
                0,
                "Initial brief seeding",
                SessionId));

        var moderatorResponse = new AgentResponse(
            AgentId.Moderator,
            "READY - All clarification complete. The brief is now clear.",
            patch,
            100,
            false,
            null);
        
        runner.RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(moderatorResponse));

        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(
            runner: runner,
            sessionService: sessionService);
        
        var state = BuildState(SessionPhase.Clarification);

        // Act
        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        // Assert
        await sessionService.Received(1).AdvancePhaseAsync(
            state,
            SessionPhase.AnalysisRound,
            Arg.Any<CancellationToken>());
        state.ClarificationIncomplete.Should().BeFalse();
    }

    [Fact]
    public async Task RunModeratorAsync_WithoutReadySignal_Should_IncrementClarificationRound()
    {
        // Arrange
        var runner = Substitute.For<IAgentRunner>();
        var moderatorResponse = new AgentResponse(
            AgentId.Moderator,
            "What is your target audience?",
            null,
            100,
            false,
            null);
        
        runner.RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(moderatorResponse));

        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(
            runner: runner,
            sessionService: sessionService);
        
        var state = BuildState(SessionPhase.Clarification);
        state.ClarificationRoundCount = 0;

        // Act
        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        // Assert
        state.ClarificationRoundCount.Should().Be(1);
        await sessionService.Received(1).RecordRoundSnapshotAsync(
            state,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunModeratorAsync_MaxRoundsReached_Should_TimeoutAndProceedWithPartialBrief()
    {
        // Arrange
        var runner = Substitute.For<IAgentRunner>();
        var moderatorResponse = new AgentResponse(
            AgentId.Moderator,
            "What is your target audience?",
            null,
            100,
            false,
            null);
        
        runner.RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(moderatorResponse));

        var sessionService = StubSessionService();
        var policy = DefaultPolicy();
        var orchestrator = BuildOrchestrator(
            runner: runner,
            sessionService: sessionService,
            policy: policy);
        
        var state = BuildState(SessionPhase.Clarification);
        state.ClarificationRoundCount = policy.MaxClarificationRounds - 1;

        // Act
        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        // Assert
        state.ClarificationRoundCount.Should().Be(policy.MaxClarificationRounds);
        state.ClarificationIncomplete.Should().BeTrue();
        await sessionService.Received(1).AdvancePhaseAsync(
            state,
            SessionPhase.AnalysisRound,
            Arg.Any<CancellationToken>());
    }
}
