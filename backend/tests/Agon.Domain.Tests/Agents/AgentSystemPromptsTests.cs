using Agon.Domain.Agents;
using FluentAssertions;

namespace Agon.Domain.Tests.Agents;

public class AgentSystemPromptsTests
{
    [Fact]
    public void SocraticClarifier_PromptContainsRoleAndGoldenTriangle()
    {
        AgentSystemPrompts.SocraticClarifier.Should().Contain("Socratic Clarifier");
        AgentSystemPrompts.SocraticClarifier.Should().Contain("Golden Triangle");
        AgentSystemPrompts.SocraticClarifier.Should().Contain("Debate Brief");
    }

    [Fact]
    public void FramingChallenger_PromptContainsRoleAndReframe()
    {
        AgentSystemPrompts.FramingChallenger.Should().Contain("Framing Challenger");
        AgentSystemPrompts.FramingChallenger.Should().Contain("reframe");
        AgentSystemPrompts.FramingChallenger.Should().Contain("problem definition");
    }

    [Fact]
    public void ProductStrategist_PromptContainsRoleAndMvp()
    {
        AgentSystemPrompts.ProductStrategist.Should().Contain("Product Strategist");
        AgentSystemPrompts.ProductStrategist.Should().Contain("MVP");
    }

    [Fact]
    public void TechnicalArchitect_PromptContainsRoleAndArchitecture()
    {
        AgentSystemPrompts.TechnicalArchitect.Should().Contain("Technical Architect");
        AgentSystemPrompts.TechnicalArchitect.Should().Contain("architecture");
    }

    [Fact]
    public void Contrarian_PromptContainsRoleAndFailureModes()
    {
        AgentSystemPrompts.Contrarian.Should().Contain("Contrarian");
        AgentSystemPrompts.Contrarian.Should().Contain("fail");
    }

    [Fact]
    public void ResearchLibrarian_PromptContainsRoleAndEvidence()
    {
        AgentSystemPrompts.ResearchLibrarian.Should().Contain("Research Librarian");
        AgentSystemPrompts.ResearchLibrarian.Should().Contain("evidence");
    }

    [Fact]
    public void SynthesisValidation_PromptContainsRoleAndConvergence()
    {
        AgentSystemPrompts.SynthesisValidation.Should().Contain("Synthesis");
        AgentSystemPrompts.SynthesisValidation.Should().Contain("convergence");
    }

    [Fact]
    public void AllPrompts_AreNonEmptyStrings()
    {
        AgentSystemPrompts.SocraticClarifier.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.FramingChallenger.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.ProductStrategist.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.TechnicalArchitect.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.Contrarian.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.ResearchLibrarian.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.SynthesisValidation.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AllPrompts_ContainPatchRules()
    {
        AgentSystemPrompts.SocraticClarifier.Should().Contain("PATCH");
        AgentSystemPrompts.FramingChallenger.Should().Contain("PATCH");
        AgentSystemPrompts.ProductStrategist.Should().Contain("PATCH");
        AgentSystemPrompts.TechnicalArchitect.Should().Contain("PATCH");
        AgentSystemPrompts.Contrarian.Should().Contain("PATCH");
        AgentSystemPrompts.ResearchLibrarian.Should().Contain("PATCH");
        AgentSystemPrompts.SynthesisValidation.Should().Contain("PATCH");
    }

    [Fact]
    public void GetPrompt_ReturnsCorrectPromptForAgentId()
    {
        AgentSystemPrompts.GetPrompt(AgentId.SocraticClarifier).Should().Be(AgentSystemPrompts.SocraticClarifier);
        AgentSystemPrompts.GetPrompt(AgentId.Contrarian).Should().Be(AgentSystemPrompts.Contrarian);
    }

    [Fact]
    public void GetPrompt_ThrowsForUnknownAgentId()
    {
        var act = () => AgentSystemPrompts.GetPrompt("unknown_agent");

        act.Should().Throw<ArgumentException>();
    }
}
