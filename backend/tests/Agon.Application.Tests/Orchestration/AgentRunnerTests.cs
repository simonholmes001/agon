using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using FluentAssertions;
using NSubstitute;

namespace Agon.Application.Tests.Orchestration;

public class AgentRunnerTests
{
    [Fact]
    public async Task ApplyValidatedPatchesAsync_AppliesInAlphabeticalAgentOrder()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        var sut = new AgentRunner(repository);
        var sessionId = Guid.NewGuid();

        var responses = new List<AgentExecutionResult>
        {
            AgentExecutionResult.Success("technical_architect", CreatePatch("technical_architect")),
            AgentExecutionResult.Success("contrarian", CreatePatch("contrarian")),
            AgentExecutionResult.Success("product_strategist", CreatePatch("product_strategist"))
        };

        await sut.ApplyValidatedPatchesAsync(sessionId, responses, CancellationToken.None);

        Received.InOrder(() =>
        {
            repository.ApplyPatchAsync(sessionId, Arg.Is<TruthMapPatch>(p => p.Meta.Agent == "contrarian"), Arg.Any<CancellationToken>());
            repository.ApplyPatchAsync(sessionId, Arg.Is<TruthMapPatch>(p => p.Meta.Agent == "product_strategist"), Arg.Any<CancellationToken>());
            repository.ApplyPatchAsync(sessionId, Arg.Is<TruthMapPatch>(p => p.Meta.Agent == "technical_architect"), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RunRoundAsync_MarksTimedOutAgent()
    {
        var repository = Substitute.For<ITruthMapRepository>();
        var sut = new AgentRunner(repository);
        var slowAgent = new SlowAgent("contrarian");

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            Round = 1,
            Phase = SessionPhase.DebateRound1,
            TruthMap = TruthMapState.CreateNew(Guid.NewGuid()),
            FrictionLevel = 50
        };

        var results = await sut.RunRoundAsync([slowAgent], context, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        results.Should().ContainSingle();
        results[0].TimedOut.Should().BeTrue();
        results[0].Patch.Should().BeNull();
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
}
