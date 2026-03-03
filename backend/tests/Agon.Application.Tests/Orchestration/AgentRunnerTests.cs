using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Agon.Application.Tests.Orchestration;

public class AgentRunnerTests
{
    [Fact]
    public async Task ApplyValidatedPatchesAsync_AppliesInAlphabeticalAgentOrder()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var logger = Substitute.For<ILogger<AgentRunner>>();
        var sut = new AgentRunner(repository, eventBroadcaster, logger);
        var sessionId = Guid.NewGuid();

        var responses = new List<AgentExecutionResult>
        {
            AgentExecutionResult.Success("gpt_agent", CreatePatch("gpt_agent")),
            AgentExecutionResult.Success("claude_agent", CreatePatch("claude_agent")),
            AgentExecutionResult.Success("gpt_agent", CreatePatch("gpt_agent"))
        };

        await sut.ApplyValidatedPatchesAsync(sessionId, responses, CancellationToken.None);

        Received.InOrder(() =>
        {
            repository.ApplyPatchAsync(sessionId, Arg.Is<TruthMapPatch>(p => p.Meta.Agent == "claude_agent"), Arg.Any<CancellationToken>());
            repository.ApplyPatchAsync(sessionId, Arg.Is<TruthMapPatch>(p => p.Meta.Agent == "gpt_agent"), Arg.Any<CancellationToken>());
            repository.ApplyPatchAsync(sessionId, Arg.Is<TruthMapPatch>(p => p.Meta.Agent == "gpt_agent"), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ApplyValidatedPatchesAsync_BroadcastsPatchEventsAfterApply()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var logger = Substitute.For<ILogger<AgentRunner>>();
        var sut = new AgentRunner(repository, eventBroadcaster, logger);
        var sessionId = Guid.NewGuid();

        var mapAfterPatch = TruthMapState.CreateNew(sessionId);
        mapAfterPatch.IncrementVersion();
        repository.GetAsync(sessionId, Arg.Any<CancellationToken>()).Returns(mapAfterPatch);

        var responses = new List<AgentExecutionResult>
        {
            AgentExecutionResult.Success("claude_agent", CreatePatch("claude_agent")),
            AgentExecutionResult.Success("gpt_agent", CreatePatch("gpt_agent"))
        };

        await sut.ApplyValidatedPatchesAsync(sessionId, responses, CancellationToken.None);

        await eventBroadcaster.Received(2).TruthMapPatchedAsync(
            sessionId,
            Arg.Any<TruthMapPatch>(),
            Arg.Is(1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyValidatedPatchesAsync_SkipsTimedOutAndMissingPatchResults()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var logger = Substitute.For<ILogger<AgentRunner>>();
        var sut = new AgentRunner(repository, eventBroadcaster, logger);
        var sessionId = Guid.NewGuid();

        var responses = new List<AgentExecutionResult>
        {
            AgentExecutionResult.Timeout("claude_agent"),
            AgentExecutionResult.Success("gpt_agent", patch: null, message: "no patch"),
            AgentExecutionResult.Success("gpt_agent", CreatePatch("gpt_agent"))
        };

        await sut.ApplyValidatedPatchesAsync(sessionId, responses, CancellationToken.None);

        await repository.Received(1).ApplyPatchAsync(
            sessionId,
            Arg.Is<TruthMapPatch>(patch => patch.Meta.Agent == "gpt_agent"),
            Arg.Any<CancellationToken>());
        await eventBroadcaster.Received(1).TruthMapPatchedAsync(
            sessionId,
            Arg.Is<TruthMapPatch>(patch => patch.Meta.Agent == "gpt_agent"),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyValidatedPatchesAsync_UsesVersionZero_WhenRepositoryReturnsNullMap()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TruthMapState?)null);
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var logger = Substitute.For<ILogger<AgentRunner>>();
        var sut = new AgentRunner(repository, eventBroadcaster, logger);
        var sessionId = Guid.NewGuid();
        var patch = CreatePatch("claude_agent");

        await sut.ApplyValidatedPatchesAsync(
            sessionId,
            [AgentExecutionResult.Success("claude_agent", patch)],
            CancellationToken.None);

        await eventBroadcaster.Received(1).TruthMapPatchedAsync(
            sessionId,
            patch,
            0,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunRoundAsync_MarksTimedOutAgent()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var logger = Substitute.For<ILogger<AgentRunner>>();
        var sut = new AgentRunner(repository, eventBroadcaster, logger);
        var slowAgent = new SlowAgent("claude_agent");

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            Round = 1,
            Phase = SessionPhase.Construction,
            TruthMap = TruthMapState.CreateNew(Guid.NewGuid()),
            FrictionLevel = 50
        };

        var results = await sut.RunRoundAsync([slowAgent], context, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        results.Should().ContainSingle();
        results[0].TimedOut.Should().BeTrue();
        results[0].Patch.Should().BeNull();
    }

    [Fact]
    public async Task RunRoundAsync_ReturnsFailedResult_WhenAgentThrowsSynchronously()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var logger = Substitute.For<ILogger<AgentRunner>>();
        var sut = new AgentRunner(repository, eventBroadcaster, logger);
        var failingAgent = new SyncFailAgent("gpt_agent");

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            Round = 1,
            Phase = SessionPhase.Construction,
            TruthMap = TruthMapState.CreateNew(Guid.NewGuid()),
            FrictionLevel = 50
        };

        var results = await sut.RunRoundAsync([failingAgent], context, TimeSpan.FromSeconds(1), CancellationToken.None);

        results.Should().ContainSingle();
        results[0].TimedOut.Should().BeFalse();
        results[0].Error.Should().Contain("missing api key");
    }

    [Fact]
    public async Task RunRoundAsync_InvokesCompletionCallback_AsEachAgentFinishes()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var logger = Substitute.For<ILogger<AgentRunner>>();
        var sut = new AgentRunner(repository, eventBroadcaster, logger);

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            Round = 1,
            Phase = SessionPhase.Construction,
            TruthMap = TruthMapState.CreateNew(Guid.NewGuid()),
            FrictionLevel = 50
        };

        var completedAgentIds = new List<string>();
        var agents = new ICouncilAgent[]
        {
            new DelayedSuccessAgent("slow-agent", TimeSpan.FromMilliseconds(80)),
            new DelayedSuccessAgent("fast-agent", TimeSpan.FromMilliseconds(10))
        };

        await sut.RunRoundAsync(
            agents,
            context,
            timeoutPerAgent: TimeSpan.FromSeconds(2),
            async (result, _) =>
            {
                completedAgentIds.Add(result.AgentId);
                await Task.CompletedTask;
            },
            CancellationToken.None);

        completedAgentIds.Should().ContainInOrder("fast-agent", "slow-agent");
    }

    [Fact]
    public async Task RunRoundAsync_Continues_WhenCompletionCallbackThrows()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var logger = Substitute.For<ILogger<AgentRunner>>();
        var sut = new AgentRunner(repository, eventBroadcaster, logger);

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            Round = 1,
            Phase = SessionPhase.Construction,
            TruthMap = TruthMapState.CreateNew(Guid.NewGuid()),
            FrictionLevel = 50
        };

        var results = await sut.RunRoundAsync(
            [new DelayedSuccessAgent("agent-1", TimeSpan.FromMilliseconds(5))],
            context,
            timeoutPerAgent: TimeSpan.FromSeconds(2),
            (_, _) => throw new InvalidOperationException("callback failed"),
            CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Error.Should().BeNull();
    }

    [Fact]
    public async Task RunRoundAsync_ReturnsFailedResult_WhenAgentThrowsAsynchronously()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var logger = Substitute.For<ILogger<AgentRunner>>();
        var sut = new AgentRunner(repository, eventBroadcaster, logger);

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            Round = 1,
            Phase = SessionPhase.Construction,
            TruthMap = TruthMapState.CreateNew(Guid.NewGuid()),
            FrictionLevel = 50
        };

        var results = await sut.RunRoundAsync(
            [new AsyncFailAgent("moderator", "upstream 500")],
            context,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        results.Should().ContainSingle();
        results[0].TimedOut.Should().BeFalse();
        results[0].Error.Should().Contain("upstream 500");
    }

    [Fact]
    public async Task RunRoundAsync_ReturnsTimeout_WhenAgentThrowsOperationCanceledWithoutCallerCancellation()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        var eventBroadcaster = Substitute.For<IEventBroadcaster>();
        var logger = Substitute.For<ILogger<AgentRunner>>();
        var sut = new AgentRunner(repository, eventBroadcaster, logger);

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            Round = 1,
            Phase = SessionPhase.Construction,
            TruthMap = TruthMapState.CreateNew(Guid.NewGuid()),
            FrictionLevel = 50
        };

        var results = await sut.RunRoundAsync(
            [new CanceledAgent("claude_agent")],
            context,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        results.Should().ContainSingle();
        results[0].TimedOut.Should().BeTrue();
        results[0].Error.Should().Be("timeout");
    }

    private static TruthMapPatch CreatePatch(string agent)
    {
        return new TruthMapPatch
        {
            Ops = new List<PatchOperation>(),
            Meta = new PatchMeta
            {
                Agent = agent,
                Round = 1,
                Reason = "test",
                SessionId = Guid.NewGuid()
            }
        };
    }

    private sealed class SlowAgent(string agentId) : ICouncilAgent
    {
        public string AgentId { get; } = agentId;
        public string ModelProvider => "fake";

        public async Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return new AgentResponse { Message = "done" };
        }

        public async IAsyncEnumerable<string> RunStreamingAsync(AgentContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            yield return "done";
        }
    }

    private sealed class SyncFailAgent(string agentId) : ICouncilAgent
    {
        public string AgentId { get; } = agentId;
        public string ModelProvider => "fake";

        public Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("missing api key");
        }

        public async IAsyncEnumerable<string> RunStreamingAsync(AgentContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("missing api key");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class DelayedSuccessAgent : ICouncilAgent
    {
        private readonly string agentId;
        private readonly TimeSpan delay;

        public DelayedSuccessAgent(string agentId, TimeSpan delay)
        {
            this.agentId = agentId;
            this.delay = delay;
        }

        public string AgentId => agentId;
        public string ModelProvider => "fake";

        public async Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new AgentResponse { Message = $"done-{agentId}" };
        }

        public async IAsyncEnumerable<string> RunStreamingAsync(AgentContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            yield return $"done-{agentId}";
        }
    }

    private sealed class AsyncFailAgent(string agentId, string error) : ICouncilAgent
    {
        public string AgentId { get; } = agentId;
        public string ModelProvider => "fake";

        public async Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException(error);
        }

        public async IAsyncEnumerable<string> RunStreamingAsync(AgentContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException(error);
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class CanceledAgent(string agentId) : ICouncilAgent
    {
        public string AgentId { get; } = agentId;
        public string ModelProvider => "fake";

        public Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken) =>
            Task.FromCanceled<AgentResponse>(new CancellationToken(canceled: true));

        public async IAsyncEnumerable<string> RunStreamingAsync(AgentContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new OperationCanceledException();
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }
}
