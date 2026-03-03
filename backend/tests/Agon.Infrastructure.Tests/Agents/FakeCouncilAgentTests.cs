using Agon.Application.Orchestration;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Agon.Infrastructure.Agents;
using FluentAssertions;

namespace Agon.Infrastructure.Tests.Agents;

public class FakeCouncilAgentTests
{
    [Fact]
    public async Task RunAsync_ReturnsConfiguredResponse()
    {
        var sessionId = Guid.NewGuid();
        var patch = new TruthMapPatch
        {
            Ops = [],
            Meta = new PatchMeta
            {
                Agent = "claude_agent",
                Round = 1,
                Reason = "fake",
                SessionId = sessionId
            }
        };

        var agent = new FakeCouncilAgent("claude_agent", "fake-provider", "MESSAGE", patch);
        var context = new AgentContext
        {
            SessionId = sessionId,
            Round = 1,
            Phase = SessionPhase.Construction,
            FrictionLevel = 50,
            TruthMap = TruthMapState.CreateNew(sessionId)
        };

        var response = await agent.RunAsync(context, CancellationToken.None);

        response.Message.Should().Be("MESSAGE");
        response.Patch.Should().NotBeNull();
        response.Patch!.Meta.Agent.Should().Be("claude_agent");
    }
}
