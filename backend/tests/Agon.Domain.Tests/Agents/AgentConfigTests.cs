using Agon.Domain.Agents;
using Agon.Domain.Sessions;
using FluentAssertions;

namespace Agon.Domain.Tests.Agents;

public class AgentConfigTests
{
    [Fact]
    public void Create_SetsAllProperties()
    {
        var config = new AgentConfig(
            AgentId: Domain.Agents.AgentId.SocraticClarifier,
            ModelProvider: "openai",
            ModelName: "gpt-5.2-thinking",
            MaxOutputTokens: 8192,
            ReasoningMode: "high",
            TimeoutSeconds: 90,
            ActivePhases: [SessionPhase.Clarification]);

        config.AgentId.Should().Be(AgentId.SocraticClarifier);
        config.ModelProvider.Should().Be("openai");
        config.ModelName.Should().Be("gpt-5.2-thinking");
        config.MaxOutputTokens.Should().Be(8192);
        config.ReasoningMode.Should().Be("high");
        config.TimeoutSeconds.Should().Be(90);
        config.ActivePhases.Should().ContainSingle()
            .Which.Should().Be(SessionPhase.Clarification);
    }

    [Fact]
    public void Defaults_HaveReasonableValues()
    {
        var config = new AgentConfig(
            AgentId: Domain.Agents.AgentId.Contrarian,
            ModelProvider: "gemini",
            ModelName: "gemini-3");

        config.MaxOutputTokens.Should().Be(4096);
        config.ReasoningMode.Should().Be("high");
        config.TimeoutSeconds.Should().Be(90);
        config.ActivePhases.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithNullActivePhases_DefaultsToEmpty()
    {
        var config = new AgentConfig(
            AgentId: Domain.Agents.AgentId.Contrarian,
            ModelProvider: "gemini",
            ModelName: "gemini-3",
            ActivePhases: null);

        config.ActivePhases.Should().NotBeNull();
        config.ActivePhases.Should().BeEmpty();
    }

    [Fact]
    public void DefaultCouncilConfigs_ContainsAllSevenAgents()
    {
        var configs = AgentConfig.DefaultCouncil;

        configs.Should().HaveCount(7);
        configs.Select(c => c.AgentId).Should().BeEquivalentTo(AgentId.AllCouncil);
    }

    [Fact]
    public void DefaultCouncilConfigs_SocraticClarifier_UsesOpenAi()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.SocraticClarifier);

        config.ModelProvider.Should().Be("openai");
        config.ModelName.Should().Be("gpt-5.2-thinking");
        config.ActivePhases.Should().Contain(SessionPhase.Clarification);
    }

    [Fact]
    public void DefaultCouncilConfigs_FramingChallenger_UsesGemini()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.FramingChallenger);

        config.ModelProvider.Should().Be("gemini");
        config.ModelName.Should().Be("gemini-3");
        config.ActivePhases.Should().Contain(SessionPhase.DebateRound1);
    }

    [Fact]
    public void DefaultCouncilConfigs_ProductStrategist_UsesAnthropic()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.ProductStrategist);

        config.ModelProvider.Should().Be("anthropic");
        config.ModelName.Should().Be("claude-opus-4.6");
    }

    [Fact]
    public void DefaultCouncilConfigs_TechnicalArchitect_UsesOpenAiTemporarily()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.TechnicalArchitect);

        config.ModelProvider.Should().Be("openai");
        config.ModelName.Should().Be("gpt-5.2-thinking");
    }

    [Fact]
    public void DefaultCouncilConfigs_Contrarian_UsesGemini()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.Contrarian);

        config.ModelProvider.Should().Be("gemini");
        config.ModelName.Should().Be("gemini-3");
    }

    [Fact]
    public void DefaultCouncilConfigs_ResearchLibrarian_UsesOpenAi()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.ResearchLibrarian);

        config.ModelProvider.Should().Be("openai");
        config.ModelName.Should().Be("gpt-5.2-thinking");
    }

    [Fact]
    public void DefaultCouncilConfigs_SynthesisValidation_UsesOpenAi()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.SynthesisValidation);

        config.ModelProvider.Should().Be("openai");
        config.ModelName.Should().Be("gpt-5.2-thinking");
        config.ActivePhases.Should().Contain(SessionPhase.Synthesis);
    }

    [Fact]
    public void DefaultCouncilConfigs_AllHaveReasoningModeHigh()
    {
        AgentConfig.DefaultCouncil
            .Should().AllSatisfy(c => c.ReasoningMode.Should().Be("high"));
    }

    [Fact]
    public void DefaultCouncilConfigs_AllHaveTimeoutOf90Seconds()
    {
        AgentConfig.DefaultCouncil
            .Should().AllSatisfy(c => c.TimeoutSeconds.Should().Be(90));
    }
}
