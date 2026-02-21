using Agon.Domain.Agents;
using FluentAssertions;

namespace Agon.Domain.Tests.Agents;

public class AgentIdTests
{
    [Fact]
    public void AgentId_ContainsAllSevenCouncilAgents()
    {
        AgentId.SocraticClarifier.Should().Be("socratic_clarifier");
        AgentId.FramingChallenger.Should().Be("framing_challenger");
        AgentId.ProductStrategist.Should().Be("product_strategist");
        AgentId.TechnicalArchitect.Should().Be("technical_architect");
        AgentId.Contrarian.Should().Be("contrarian");
        AgentId.ResearchLibrarian.Should().Be("research_librarian");
        AgentId.SynthesisValidation.Should().Be("synthesis_validation");
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
    public void AgentId_All_ReturnsAllSevenCouncilAgents()
    {
        var all = AgentId.AllCouncil;

        all.Should().HaveCount(7);
        all.Should().Contain(AgentId.SocraticClarifier);
        all.Should().Contain(AgentId.FramingChallenger);
        all.Should().Contain(AgentId.ProductStrategist);
        all.Should().Contain(AgentId.TechnicalArchitect);
        all.Should().Contain(AgentId.Contrarian);
        all.Should().Contain(AgentId.ResearchLibrarian);
        all.Should().Contain(AgentId.SynthesisValidation);
    }

    [Theory]
    [InlineData("socratic_clarifier", true)]
    [InlineData("contrarian", true)]
    [InlineData("orchestrator", false)]
    [InlineData("user", false)]
    [InlineData("unknown_agent", false)]
    public void AgentId_IsCouncilAgent_CorrectlyIdentifiesCouncilMembers(string agentId, bool expected)
    {
        AgentId.IsCouncilAgent(agentId).Should().Be(expected);
    }
}
