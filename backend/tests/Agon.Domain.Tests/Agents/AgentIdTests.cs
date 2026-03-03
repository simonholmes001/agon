using Agon.Domain.Agents;
using FluentAssertions;

namespace Agon.Domain.Tests.Agents;

public class AgentIdTests
{
    [Fact]
    public void AgentId_ContainsAllSixCouncilAgents()
    {
        AgentId.Moderator.Should().Be("moderator");
        AgentId.GptAgent.Should().Be("gpt_agent");
        AgentId.GeminiAgent.Should().Be("gemini_agent");
        AgentId.ClaudeAgent.Should().Be("claude_agent");
        AgentId.CritiqueAgent.Should().Be("critique_agent");
        AgentId.Synthesizer.Should().Be("synthesizer");
    }

    [Fact]
    public void AgentId_Orchestrator_IsDefinedSeparately()
    {
        AgentId.Orchestrator.Should().Be("orchestrator");
    }

    [Fact]
    public void AgentId_User_IsDefinedForHitlPatches()
    {
        AgentId.User.Should().Be("user");
    }

    [Fact]
    public void AgentId_All_ReturnsAllSixCouncilAgents()
    {
        var all = AgentId.AllCouncil;

        all.Should().HaveCount(6);
        all.Should().Contain(AgentId.Moderator);
        all.Should().Contain(AgentId.GptAgent);
        all.Should().Contain(AgentId.GeminiAgent);
        all.Should().Contain(AgentId.ClaudeAgent);
        all.Should().Contain(AgentId.CritiqueAgent);
        all.Should().Contain(AgentId.Synthesizer);
    }

    [Fact]
    public void AgentId_WorkingAgents_ReturnsThreeWorkingAgents()
    {
        var working = AgentId.WorkingAgents;

        working.Should().HaveCount(3);
        working.Should().Contain(AgentId.GptAgent);
        working.Should().Contain(AgentId.GeminiAgent);
        working.Should().Contain(AgentId.ClaudeAgent);
    }

    [Theory]
    [InlineData("moderator", true)]
    [InlineData("gpt_agent", true)]
    [InlineData("gemini_agent", true)]
    [InlineData("claude_agent", true)]
    [InlineData("critique_agent", true)]
    [InlineData("synthesizer", true)]
    [InlineData("orchestrator", false)]
    [InlineData("user", false)]
    [InlineData("unknown_agent", false)]
    public void AgentId_IsCouncilAgent_CorrectlyIdentifiesCouncilMembers(string agentId, bool expected)
    {
        AgentId.IsCouncilAgent(agentId).Should().Be(expected);
    }
}
