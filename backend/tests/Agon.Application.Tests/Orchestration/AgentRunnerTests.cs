using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Orchestration;
using Agon.Application.Services;
using Agon.Domain.Agents;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;
using NSubstitute;
using System.Diagnostics;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Application.Tests.Orchestration;

public class AgentRunnerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TruthMapModel EmptyMap() => TruthMapModel.Empty(SessionId);

    private static TruthMapPatch EmptyPatch(string agentId) =>
        new([], new PatchMeta(agentId, 1, "Test patch", SessionId));

    private static AgentResponse SuccessResponse(string agentId, TruthMapPatch? patch = null) =>
        new(agentId, $"Message from {agentId}", patch ?? EmptyPatch(agentId), 100, false, null);

    private static ICouncilAgent StubAgent(string agentId, AgentResponse response)
    {
        var agent = Substitute.For<ICouncilAgent>();
        agent.AgentId.Returns(agentId);
        agent.ModelProvider.Returns("fake/model");
        agent.RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(response));
        return agent;
    }

    private static ICouncilAgent TimingOutAgent(string agentId, int timeoutSeconds = 1)
    {
        var agent = Substitute.For<ICouncilAgent>();
        agent.AgentId.Returns(agentId);
        agent.ModelProvider.Returns("fake/model");
        agent.RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>())
             .Returns(async call =>
             {
                 var ct = call.ArgAt<CancellationToken>(1);
                 await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds + 5), ct);
                 return SuccessResponse(agentId);
             });
        return agent;
    }

    private static ITruthMapRepository StubRepo(TruthMapModel? returnMap = null)
    {
        var repo = Substitute.For<ITruthMapRepository>();
        var map = returnMap ?? EmptyMap();
        repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TruthMapModel?>(map));
        repo.ApplyPatchAsync(Arg.Any<Guid>(), Arg.Any<TruthMapPatch>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(map with { Version = map.Version + 1 }));
        return repo;
    }

    private static IEventBroadcaster NullBroadcaster()
    {
        var b = Substitute.For<IEventBroadcaster>();
        return b;
    }

    private static ConversationHistoryService StubConversationHistory()
    {
        var repo = Substitute.For<IAgentMessageRepository>();
        return new ConversationHistoryService(repo);
    }

    private static AgentRunner BuildRunner(
        IReadOnlyList<ICouncilAgent> agents,
        ITruthMapRepository? repo = null,
        IEventBroadcaster? broadcaster = null,
        ConversationHistoryService? conversationHistory = null,
        int agentTimeoutSeconds = 5)
    {
        return new AgentRunner(
            agents,
            repo ?? StubRepo(),
            broadcaster ?? NullBroadcaster(),
            conversationHistory ?? StubConversationHistory(),
            agentTimeoutSeconds);
    }

    // ── Analysis Round — all agents called in parallel ────────────────────────

    [Fact]
    public async Task RunAnalysisRoundAsync_CallsAllCouncilAgents()
    {
        var claude = StubAgent(AgentId.ClaudeAgent, SuccessResponse(AgentId.ClaudeAgent));
        var gemini = StubAgent(AgentId.GeminiAgent, SuccessResponse(AgentId.GeminiAgent));
        var gpt = StubAgent(AgentId.GptAgent, SuccessResponse(AgentId.GptAgent));

        var runner = BuildRunner([claude, gemini, gpt]);
        var state = BuildSessionState();

        await runner.RunAnalysisRoundAsync(state, CancellationToken.None);

        await claude.Received(1).RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>());
        await gemini.Received(1).RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>());
        await gpt.Received(1).RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_AgentsReceiveAnalysisPhaseContext()
    {
        var capturedContexts = new List<AgentContext>();
        var agent = Substitute.For<ICouncilAgent>();
        agent.AgentId.Returns(AgentId.GptAgent);
        agent.RunAsync(Arg.Do<AgentContext>(capturedContexts.Add), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(SuccessResponse(AgentId.GptAgent)));

        var runner = BuildRunner([agent]);
        var state = BuildSessionState(frictionLevel: 40, round: 1);

        await runner.RunAnalysisRoundAsync(state, CancellationToken.None);

        capturedContexts.Should().HaveCount(1);
        capturedContexts[0].Phase.Should().Be(SessionPhase.AnalysisRound);
        capturedContexts[0].FrictionLevel.Should().Be(40);
        capturedContexts[0].RoundNumber.Should().Be(1);
        capturedContexts[0].CritiqueTargetMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_ReturnsResponsesForAllAgents()
    {
        var claude = StubAgent(AgentId.ClaudeAgent, SuccessResponse(AgentId.ClaudeAgent));
        var gemini = StubAgent(AgentId.GeminiAgent, SuccessResponse(AgentId.GeminiAgent));
        var gpt = StubAgent(AgentId.GptAgent, SuccessResponse(AgentId.GptAgent));

        var runner = BuildRunner([claude, gemini, gpt]);
        var state = BuildSessionState();

        var responses = await runner.RunAnalysisRoundAsync(state, CancellationToken.None);

        responses.Should().HaveCount(3);
        responses.Select(r => r.AgentId).Should().BeEquivalentTo(
            [AgentId.ClaudeAgent, AgentId.GeminiAgent, AgentId.GptAgent]);
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_TimedOutAgent_ReturnsTimedOutResponse()
    {
        var claude = StubAgent(AgentId.ClaudeAgent, SuccessResponse(AgentId.ClaudeAgent));
        var slowGpt = TimingOutAgent(AgentId.GptAgent, timeoutSeconds: 1);

        var runner = BuildRunner([claude, slowGpt], agentTimeoutSeconds: 1);
        var state = BuildSessionState();

        var responses = await runner.RunAnalysisRoundAsync(state, CancellationToken.None);

        var gptResponse = responses.Single(r => r.AgentId == AgentId.GptAgent);
        gptResponse.TimedOut.Should().BeTrue();
        var claudeResponse = responses.Single(r => r.AgentId == AgentId.ClaudeAgent);
        claudeResponse.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_NonCooperativeAgent_HardTimeoutStillReturnsTimedOutResponse()
    {
        var claude = StubAgent(AgentId.ClaudeAgent, SuccessResponse(AgentId.ClaudeAgent));

        var hungAgent = Substitute.For<ICouncilAgent>();
        hungAgent.AgentId.Returns(AgentId.GptAgent);
        hungAgent.ModelProvider.Returns("fake/model");
        hungAgent.RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => new TaskCompletionSource<AgentResponse>().Task);

        var runner = BuildRunner([claude, hungAgent], agentTimeoutSeconds: 1);
        var state = BuildSessionState();

        var stopwatch = Stopwatch.StartNew();
        var responses = await runner.RunAnalysisRoundAsync(state, CancellationToken.None);
        stopwatch.Stop();

        responses.Single(r => r.AgentId == AgentId.GptAgent).TimedOut.Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    // ── Critique Round — cross-agent assignment ────────────────────────────────

    [Fact]
    public async Task RunCritiqueRoundAsync_EachAgentReceivesOtherTwoMessagesOnly()
    {
        var capturedContexts = new Dictionary<string, AgentContext>();

        var claude = BuildCapturingAgent(AgentId.ClaudeAgent, capturedContexts);
        var gemini = BuildCapturingAgent(AgentId.GeminiAgent, capturedContexts);
        var gpt = BuildCapturingAgent(AgentId.GptAgent, capturedContexts);

        var priorMessages = new Dictionary<string, string>
        {
            [AgentId.ClaudeAgent] = "Claude's analysis",
            [AgentId.GeminiAgent] = "Gemini's analysis",
            [AgentId.GptAgent] = "GPT's analysis"
        };

        var runner = BuildRunner([claude, gemini, gpt]);
        var state = BuildSessionState();
        state.LastRoundMessages.Clear();
        foreach (var (k, v) in priorMessages) state.LastRoundMessages[k] = v;

        await runner.RunCritiqueRoundAsync(state, CancellationToken.None);

        // Claude critiques Gemini + GPT — NOT itself
        capturedContexts[AgentId.ClaudeAgent].CritiqueTargetMessages
            .Select(m => m.AgentId)
            .Should().BeEquivalentTo([AgentId.GeminiAgent, AgentId.GptAgent]);

        // Gemini critiques Claude + GPT
        capturedContexts[AgentId.GeminiAgent].CritiqueTargetMessages
            .Select(m => m.AgentId)
            .Should().BeEquivalentTo([AgentId.ClaudeAgent, AgentId.GptAgent]);

        // GPT critiques Claude + Gemini
        capturedContexts[AgentId.GptAgent].CritiqueTargetMessages
            .Select(m => m.AgentId)
            .Should().BeEquivalentTo([AgentId.ClaudeAgent, AgentId.GeminiAgent]);
    }

    [Fact]
    public async Task RunCritiqueRoundAsync_AgentsReceiveCritiquePhaseContext()
    {
        var capturedContexts = new Dictionary<string, AgentContext>();
        var gpt = BuildCapturingAgent(AgentId.GptAgent, capturedContexts);
        var claude = BuildCapturingAgent(AgentId.ClaudeAgent, capturedContexts);

        var runner = BuildRunner([gpt, claude]);
        var state = BuildSessionState(frictionLevel: 80, round: 2);
        state.LastRoundMessages[AgentId.GptAgent] = "GPT msg";
        state.LastRoundMessages[AgentId.ClaudeAgent] = "Claude msg";

        await runner.RunCritiqueRoundAsync(state, CancellationToken.None);

        capturedContexts[AgentId.GptAgent].Phase.Should().Be(SessionPhase.Critique);
        capturedContexts[AgentId.GptAgent].FrictionLevel.Should().Be(80);
        capturedContexts[AgentId.GptAgent].RoundNumber.Should().Be(2);
    }

    // ── Patch application — deterministic order ───────────────────────────────

    [Fact]
    public async Task RunAnalysisRoundAsync_PatchesAppliedInAlphabeticalAgentIdOrder()
    {
        var appliedOrder = new List<string>();
        var repo = Substitute.For<ITruthMapRepository>();
        var map = EmptyMap();
        repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TruthMapModel?>(map));
        repo.ApplyPatchAsync(Arg.Any<Guid>(), Arg.Any<TruthMapPatch>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var patch = call.ArgAt<TruthMapPatch>(1);
                appliedOrder.Add(patch.Meta.Agent);
                return Task.FromResult(map with { Version = map.Version + 1 });
            });

        var claude = StubAgent(AgentId.ClaudeAgent, SuccessResponse(AgentId.ClaudeAgent));
        var gemini = StubAgent(AgentId.GeminiAgent, SuccessResponse(AgentId.GeminiAgent));
        var gpt = StubAgent(AgentId.GptAgent, SuccessResponse(AgentId.GptAgent));

        var runner = BuildRunner([gpt, claude, gemini], repo); // intentionally shuffled input
        var state = BuildSessionState();

        await runner.RunAnalysisRoundAsync(state, CancellationToken.None);

        // Must be alphabetical regardless of input order
        appliedOrder.Should().Equal(AgentId.ClaudeAgent, AgentId.GeminiAgent, AgentId.GptAgent);
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_InvalidPatch_IsRejectedAndNotApplied()
    {
        // A patch that references a non-existent entity (rule 1 violation)
        var badPatch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Replace, "/claims/nonexistent-id/status", "contested")],
            new PatchMeta(AgentId.GptAgent, 1, "Bad patch", SessionId));

        var gpt = StubAgent(AgentId.GptAgent, new AgentResponse(
            AgentId.GptAgent, "Message", badPatch, 100, false, null));

        var repo = Substitute.For<ITruthMapRepository>();
        var map = EmptyMap();
        repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TruthMapModel?>(map));

        var runner = BuildRunner([gpt], repo);
        var state = BuildSessionState();

        // Should not throw — bad patches are logged and skipped
        var responses = await runner.RunAnalysisRoundAsync(state, CancellationToken.None);

        await repo.DidNotReceive().ApplyPatchAsync(
            Arg.Any<Guid>(), Arg.Any<TruthMapPatch>(), Arg.Any<CancellationToken>());
        responses.Should().HaveCount(1);
    }

    [Fact]
    public async Task RunAnalysisRoundAsync_NullPatch_DoesNotCallApplyPatch()
    {
        var responseWithNoPatch = new AgentResponse(
            AgentId.GptAgent, "Message only", null, 100, false, null);

        var gpt = StubAgent(AgentId.GptAgent, responseWithNoPatch);
        var repo = Substitute.For<ITruthMapRepository>();
        var map = EmptyMap();
        repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TruthMapModel?>(map));

        var runner = BuildRunner([gpt], repo);
        var state = BuildSessionState();

        await runner.RunAnalysisRoundAsync(state, CancellationToken.None);

        await repo.DidNotReceive().ApplyPatchAsync(
            Arg.Any<Guid>(), Arg.Any<TruthMapPatch>(), Arg.Any<CancellationToken>());
    }

    // ── Targeted loop ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunTargetedLoopAsync_AgentsReceiveTargetedLoopPhaseContext()
    {
        var capturedContexts = new List<AgentContext>();
        var agent = Substitute.For<ICouncilAgent>();
        agent.AgentId.Returns(AgentId.GptAgent);
        agent.RunAsync(Arg.Do<AgentContext>(capturedContexts.Add), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(SuccessResponse(AgentId.GptAgent)));

        var runner = BuildRunner([agent]);
        var state = BuildSessionState();
        const string directive = "Revisit feasibility claims.";

        await runner.RunTargetedLoopAsync(state, [AgentId.GptAgent], directive, CancellationToken.None);

        capturedContexts.Should().HaveCount(1);
        capturedContexts[0].Phase.Should().Be(SessionPhase.TargetedLoop);
        capturedContexts[0].MicroDirective.Should().Be(directive);
    }

    [Fact]
    public async Task RunTargetedLoopAsync_OnlyTargetedAgentsAreCalled()
    {
        var claude = StubAgent(AgentId.ClaudeAgent, SuccessResponse(AgentId.ClaudeAgent));
        var gpt = StubAgent(AgentId.GptAgent, SuccessResponse(AgentId.GptAgent));

        var runner = BuildRunner([claude, gpt]);
        var state = BuildSessionState();

        await runner.RunTargetedLoopAsync(
            state, [AgentId.ClaudeAgent], "Only Claude targeted.", CancellationToken.None);

        await claude.Received(1).RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>());
        await gpt.DidNotReceive().RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>());
    }

    // ── Budget tracking ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunAnalysisRoundAsync_UpdatesTokensUsedOnState()
    {
        var claude = StubAgent(AgentId.ClaudeAgent,
            new AgentResponse(AgentId.ClaudeAgent, "Msg", EmptyPatch(AgentId.ClaudeAgent), 250, false, null));
        var gpt = StubAgent(AgentId.GptAgent,
            new AgentResponse(AgentId.GptAgent, "Msg", EmptyPatch(AgentId.GptAgent), 150, false, null));

        var runner = BuildRunner([claude, gpt]);
        var state = BuildSessionState();
        state.TokensUsed = 1000;

        await runner.RunAnalysisRoundAsync(state, CancellationToken.None);

        state.TokensUsed.Should().Be(1400); // 1000 + 250 + 150
    }

    // ── Moderator Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunModeratorAsync_Should_UseForClarificationContext()
    {
        // Arrange
        var capturedContext = default(AgentContext);
        var moderator = Substitute.For<ICouncilAgent>();
        moderator.AgentId.Returns(AgentId.Moderator);
        moderator.ModelProvider.Returns("fake/model");
        moderator.RunAsync(Arg.Do<AgentContext>(ctx => capturedContext = ctx), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult(SuccessResponse(AgentId.Moderator)));

        var runner = BuildRunner([moderator]);
        var state = BuildSessionState();
        state.Phase = SessionPhase.Clarification;
        state.ClarificationRoundCount = 2;
        
        // Add user messages to state
        state.UserMessages.Add(new UserMessage(
            "Target customers are small retail businesses",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            1));
        state.UserMessages.Add(new UserMessage(
            "Primary pain point is inventory management",
            DateTimeOffset.UtcNow.AddMinutes(-3),
            1));
        state.UserMessages.Add(new UserMessage(
            "Budget is around $10k",
            DateTimeOffset.UtcNow,
            2));

        // Act
        await runner.RunModeratorAsync(state, CancellationToken.None);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.Phase.Should().Be(SessionPhase.Clarification);
        capturedContext.RoundNumber.Should().Be(2);
        capturedContext.UserMessages.Should().HaveCount(3);
        capturedContext.UserMessages[0].Content.Should().Be("Target customers are small retail businesses");
        capturedContext.UserMessages[1].Content.Should().Be("Primary pain point is inventory management");
        capturedContext.UserMessages[2].Content.Should().Be("Budget is around $10k");
    }

    [Fact]
    public async Task RunModeratorAsync_Should_WorkWithEmptyUserMessages()
    {
        // Arrange
        var capturedContext = default(AgentContext);
        var moderator = Substitute.For<ICouncilAgent>();
        moderator.AgentId.Returns(AgentId.Moderator);
        moderator.ModelProvider.Returns("fake/model");
        moderator.RunAsync(Arg.Do<AgentContext>(ctx => capturedContext = ctx), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult(SuccessResponse(AgentId.Moderator)));

        var runner = BuildRunner([moderator]);
        var state = BuildSessionState();
        state.Phase = SessionPhase.Clarification;

        // Act
        await runner.RunModeratorAsync(state, CancellationToken.None);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.UserMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task RunModeratorAsync_Should_ApplyPatchFromModeratorResponse()
    {
        // Arrange
        var repo = StubRepo();
        var patch = new TruthMapPatch(
            [new PatchOperation(PatchOp.Add, "/topics/-", "New Topic")],
            new PatchMeta(AgentId.Moderator, 1, "Moderator patch", SessionId));
        var moderatorResponse = new AgentResponse(
            AgentId.Moderator,
            "READY",
            patch,
            100,
            false,
            null);

        var moderator = Substitute.For<ICouncilAgent>();
        moderator.AgentId.Returns(AgentId.Moderator);
        moderator.ModelProvider.Returns("fake/model");
        moderator.RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult(moderatorResponse));

        var runner = BuildRunner([moderator], repo: repo);
        var state = BuildSessionState();
        state.Phase = SessionPhase.Clarification;

        // Act
        await runner.RunModeratorAsync(state, CancellationToken.None);

        // Assert
        await repo.Received(1).ApplyPatchAsync(SessionId, patch, Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SessionState BuildSessionState(int frictionLevel = 50, int round = 1)
    {
        var state = SessionState.Create(SessionId, frictionLevel, false, EmptyMap());
        state.CurrentRound = round;
        return state;
    }

    private static ICouncilAgent BuildCapturingAgent(
        string agentId,
        Dictionary<string, AgentContext> captured)
    {
        var agent = Substitute.For<ICouncilAgent>();
        agent.AgentId.Returns(agentId);
        agent.RunAsync(Arg.Do<AgentContext>(ctx => captured[agentId] = ctx), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(SuccessResponse(agentId)));
        return agent;
    }
}
