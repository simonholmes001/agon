using Agon.Application.Models;
using Agon.Application.Orchestration;
using Agon.Application.Services;
using Agon.Domain.Agents;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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

        runner.RunPostDeliveryFollowUpAsync(
                Arg.Any<SessionState>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(
                  new AgentResponse(
                      AgentId.PostDeliveryAssistant,
                      "Post-delivery response",
                      null,
                      120,
                      false,
                      null)));

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
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        return new Orchestrator(
            runner ?? StubRunner(),
            sessionService ?? StubSessionService(),
            scopeFactory,
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

    // ── RunPostDeliveryFollowUpAsync ─────────────────────────────────────────

    [Fact]
    public async Task RunPostDeliveryFollowUpAsync_WhenInDeliver_TransitionsToPostDeliveryAndCallsRunner()
    {
        var runner = StubRunner();
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(runner: runner, sessionService: sessionService);
        var state = BuildState(SessionPhase.Deliver);

        await orchestrator.RunPostDeliveryFollowUpAsync(state, "Please shorten section 2", CancellationToken.None);

        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.PostDelivery, Arg.Any<CancellationToken>());
        await runner.Received(1).RunPostDeliveryFollowUpAsync(
            state,
            "Please shorten section 2",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPostDeliveryFollowUpAsync_WhenAlreadyPostDelivery_DoesNotReAdvancePhase()
    {
        var runner = StubRunner();
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(runner: runner, sessionService: sessionService);
        var state = BuildState(SessionPhase.PostDelivery);

        await orchestrator.RunPostDeliveryFollowUpAsync(state, "Refine the acceptance criteria", CancellationToken.None);

        await sessionService.DidNotReceive().AdvancePhaseAsync(
            state, SessionPhase.PostDelivery, Arg.Any<CancellationToken>());
        await runner.Received(1).RunPostDeliveryFollowUpAsync(
            state,
            "Refine the acceptance criteria",
            Arg.Any<CancellationToken>());
    }

    // ── RunFullDebateChainAsync ──────────────────────────────────────────────

    [Fact]
    public async Task RunFullDebateChainAsync_WhenUnexpectedErrorOccurs_TerminatesWithGaps()
    {
        var runner = Substitute.For<IAgentRunner>();
        runner.RunAnalysisRoundAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<AgentResponse>>>(_ => throw new InvalidOperationException("boom"));

        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(runner: runner, sessionService: sessionService);
        var state = BuildState(SessionPhase.AnalysisRound);

        await orchestrator.RunFullDebateChainAsync(state, CancellationToken.None);

        state.Status.Should().Be(SessionStatus.CompleteWithGaps);
        await sessionService.Received(1).AdvancePhaseAsync(
            state, SessionPhase.DeliverWithGaps, Arg.Any<CancellationToken>());
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
        var runner = StubRunner();
        var orchestrator = BuildOrchestrator(runner: runner, sessionService: sessionService);
        var state = BuildState(SessionPhase.AnalysisRound);

        await orchestrator.RunAnalysisRoundAsync(state, CancellationToken.None);

        // Verify the analysis agent runner was called (the snapshot is recorded by the chain)
        await runner.Received(1).RunAnalysisRoundAsync(state, Arg.Any<CancellationToken>());
        // At minimum, one snapshot must have been recorded
        await sessionService.ReceivedWithAnyArgs().RecordRoundSnapshotAsync(default!, default);
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_AdvancesToCritique_WhenBudgetNotExhausted()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.AnalysisRound);

        await orchestrator.RunAnalysisRoundAsync(state, CancellationToken.None);

        // Analysis must transition to Critique as part of the chain
        await sessionService.ReceivedWithAnyArgs().AdvancePhaseAsync(
            default!, default, default);
        await sessionService.Received().AdvancePhaseAsync(
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
        var runner = StubRunner();
        var orchestrator = BuildOrchestrator(runner: runner, sessionService: sessionService);
        var state = BuildState(SessionPhase.Critique);

        await orchestrator.RunCritiqueRoundAsync(state, CancellationToken.None);

        // Critique runner must have been called
        await runner.Received(1).RunCritiqueRoundAsync(state, Arg.Any<CancellationToken>());
        // At minimum one snapshot recorded
        await sessionService.ReceivedWithAnyArgs().RecordRoundSnapshotAsync(default!, default);
    }

    [Fact]
    public async Task RunCritiqueRoundAsync_AdvancesToSynthesis_WhenBudgetNotExhausted()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.Critique);

        await orchestrator.RunCritiqueRoundAsync(state, CancellationToken.None);

        // Critique must transition to Synthesis
        await sessionService.Received().AdvancePhaseAsync(
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

        // Synthesis runner must be called (chain may call it multiple times via targeted loops)
        await runner.ReceivedWithAnyArgs().RunSynthesisAsync(default!, default);
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

        // TargetedLoop must be entered at least once (chain will exhaust MaxTargetedLoops=2)
        await sessionService.Received().AdvancePhaseAsync(
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
        var policy = new RoundPolicy { MaxTargetedLoops = 2 };
        var orchestrator = BuildOrchestrator(policy: policy);
        var state = BuildState(SessionPhase.TargetedLoop);
        // Set to MaxTargetedLoops-1 so the chain terminates after one synthesis re-entry
        state.TargetedLoopCount = 1;

        await orchestrator.RunTargetedLoopAsync(
            state, [AgentId.ClaudeAgent], "Fix coherence.", CancellationToken.None);

        // Must have been incremented at least once (to 2); chain won't loop again since 2 >= MaxTargetedLoops
        state.TargetedLoopCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task RunTargetedLoopAsync_IncrementsRoundCounter()
    {
        var policy = new RoundPolicy { MaxTargetedLoops = 1 };
        var orchestrator = BuildOrchestrator(policy: policy);
        var state = BuildState(SessionPhase.TargetedLoop);
        // MaxTargetedLoops=1 so after one targeted loop + synthesis, chain terminates
        state.TargetedLoopCount = 0;
        state.CurrentRound = 3;

        await orchestrator.RunTargetedLoopAsync(
            state, [AgentId.ClaudeAgent], "Fix coherence.", CancellationToken.None);

        state.CurrentRound.Should().Be(4);
    }

    [Fact]
    public async Task RunTargetedLoopAsync_RecordsSnapshot()
    {
        var sessionService = StubSessionService();
        var runner = StubRunner();
        var orchestrator = BuildOrchestrator(runner: runner, sessionService: sessionService);
        var state = BuildState(SessionPhase.TargetedLoop);

        await orchestrator.RunTargetedLoopAsync(
            state, [AgentId.GptAgent], "Gap directive.", CancellationToken.None);

        // The targeted loop agent runner must have been called (snapshot recorded within the loop)
        await runner.Received(1).RunTargetedLoopAsync(
            state,
            Arg.Any<IReadOnlyList<string>>(),
            "Gap directive.",
            Arg.Any<CancellationToken>());
        await sessionService.ReceivedWithAnyArgs().RecordRoundSnapshotAsync(default!, default);
    }

    [Fact]
    public async Task RunTargetedLoopAsync_AdvancesToSynthesisAfterCompletion()
    {
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(sessionService: sessionService);
        var state = BuildState(SessionPhase.TargetedLoop);

        await orchestrator.RunTargetedLoopAsync(
            state, [AgentId.GptAgent], "Gap directive.", CancellationToken.None);

        // Must advance to Synthesis at least once after the targeted loop
        await sessionService.Received().AdvancePhaseAsync(
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
            "STATUS: READY\nAll clarification complete. The brief is now clear.",
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
        state.ClarificationRoundCount = 1;

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
    public async Task RunModeratorAsync_WithDirectAnswerStatus_ShouldNotAdvanceOrIncrement()
    {
        var runner = Substitute.For<IAgentRunner>();
        var moderatorResponse = new AgentResponse(
            AgentId.Moderator,
            "STATUS: DIRECT_ANSWER\nAgon can help with planning, coding, writing, and analysis.",
            null,
            70,
            false,
            null);

        runner.RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(moderatorResponse));

        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(
            runner: runner,
            sessionService: sessionService);

        var state = BuildState(SessionPhase.Clarification);
        state.ClarificationRoundCount = 2;

        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        state.ClarificationRoundCount.Should().Be(2);
        await sessionService.DidNotReceive().AdvancePhaseAsync(
            state,
            SessionPhase.AnalysisRound,
            Arg.Any<CancellationToken>());
        await sessionService.DidNotReceive().RecordRoundSnapshotAsync(
            state,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunModeratorAsync_WithReadyButSimpleMetaQuery_ShouldSuppressDebateChain()
    {
        var runner = Substitute.For<IAgentRunner>();
        var moderatorResponse = new AgentResponse(
            AgentId.Moderator,
            "STATUS: READY\nAgon can help with planning, coding, writing, and analysis.",
            null,
            80,
            false,
            null);

        runner.RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(moderatorResponse));

        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(
            runner: runner,
            sessionService: sessionService);

        var state = BuildState(SessionPhase.Clarification);
        state.ClarificationRoundCount = 1;
        state.UserMessages.Add(new UserMessage(
            "How can Agon help me?",
            DateTimeOffset.UtcNow,
            1));

        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        state.ClarificationRoundCount.Should().Be(1);
        await sessionService.DidNotReceive().AdvancePhaseAsync(
            state,
            SessionPhase.AnalysisRound,
            Arg.Any<CancellationToken>());
        await sessionService.DidNotReceive().RecordRoundSnapshotAsync(
            state,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunModeratorAsync_WithReadyAndShortGeneralQuestion_ShouldSuppressDebateChain()
    {
        var runner = Substitute.For<IAgentRunner>();
        var moderatorResponse = new AgentResponse(
            AgentId.Moderator,
            "STATUS: READY\nDNS maps hostnames to IP addresses.",
            null,
            80,
            false,
            null);

        runner.RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(moderatorResponse));

        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(
            runner: runner,
            sessionService: sessionService);

        var state = BuildState(SessionPhase.Clarification);
        state.ClarificationRoundCount = 1;
        state.UserMessages.Add(new UserMessage(
            "What is DNS?",
            DateTimeOffset.UtcNow,
            1));

        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        state.ClarificationRoundCount.Should().Be(1);
        await sessionService.DidNotReceive().AdvancePhaseAsync(
            state,
            SessionPhase.AnalysisRound,
            Arg.Any<CancellationToken>());
        await sessionService.DidNotReceive().RecordRoundSnapshotAsync(
            state,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunModeratorAsync_ReadyOnFirstRoundWithoutUserMessages_ShouldNotAdvance()
    {
        var runner = Substitute.For<IAgentRunner>();
        var moderatorResponse = new AgentResponse(
            AgentId.Moderator,
            "STATUS: READY\nProceed immediately.",
            null,
            80,
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

        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        state.ClarificationRoundCount.Should().Be(1);
        await sessionService.DidNotReceive().AdvancePhaseAsync(
            state,
            SessionPhase.AnalysisRound,
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

    // ── RunModeratorAsync (User Message Handling) ─────────────────────────────

    [Fact]
    public async Task RunModeratorAsync_Should_PassUserMessagesToAgentRunner()
    {
        // Arrange
        var runner = StubRunner();
        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(runner: runner, sessionService: sessionService);
        var state = BuildState(SessionPhase.Clarification);
        
        // Add user messages to state
        state.UserMessages.Add(new UserMessage(
            "Target customers are small retail businesses",
            DateTimeOffset.UtcNow,
            1));
        state.UserMessages.Add(new UserMessage(
            "Primary pain point is inventory tracking",
            DateTimeOffset.UtcNow,
            1));

        // Act
        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        // Assert
        await runner.Received(1).RunModeratorAsync(
            Arg.Is<SessionState>(s => s.UserMessages.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunModeratorAsync_Should_IncludeUserMessagesInContext()
    {
        // Arrange
        var moderatorResponse = new AgentResponse(
            AgentId: AgentId.Moderator,
            Message: "STATUS: READY\nReady to proceed.",
            Patch: null,
            TokensUsed: 100,
            TimedOut: false,
            RawOutput: null);

        var runner = Substitute.For<IAgentRunner>();
        runner.RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(moderatorResponse));

        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(runner: runner, sessionService: sessionService);
        var state = BuildState(SessionPhase.Clarification);
        state.ClarificationRoundCount = 1;
        
        state.UserMessages.Add(new UserMessage(
            "Test message",
            DateTimeOffset.UtcNow,
            1));

        // Act
        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        // Assert - Agent runner should have been called with state containing user messages
        await runner.Received(1).RunModeratorAsync(
            Arg.Is<SessionState>(s => s.UserMessages.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunModeratorAsync_Should_WorkWithEmptyUserMessages()
    {
        // Arrange
        var moderatorResponse = new AgentResponse(
            AgentId: AgentId.Moderator,
            Message: "What is your target customer?",
            Patch: null,
            TokensUsed: 50,
            TimedOut: false,
            RawOutput: null);

        var runner = Substitute.For<IAgentRunner>();
        runner.RunModeratorAsync(Arg.Any<SessionState>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(moderatorResponse));

        var sessionService = StubSessionService();
        var orchestrator = BuildOrchestrator(runner: runner, sessionService: sessionService);
        var state = BuildState(SessionPhase.Clarification);

        // Act
        await orchestrator.RunModeratorAsync(state, CancellationToken.None);

        // Assert - Should work fine with no user messages
        await runner.Received(1).RunModeratorAsync(
            Arg.Is<SessionState>(s => s.UserMessages.Count == 0),
            Arg.Any<CancellationToken>());
    }
}
