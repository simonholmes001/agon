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
            AgentId: Domain.Agents.AgentId.Moderator,
            ModelProvider: "openai",
            ModelName: "gpt-5.2",
            MaxOutputTokens: 8192,
            ReasoningMode: "high",
            TimeoutSeconds: 90,
            ActivePhases: [SessionPhase.Clarification]);

        config.AgentId.Should().Be(AgentId.Moderator);
        config.ModelProvider.Should().Be("openai");
        config.ModelName.Should().Be("gpt-5.2");
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
            AgentId: Domain.Agents.AgentId.GeminiAgent,
            ModelProvider: "gemini",
            ModelName: "gemini-3.1-pro-preview");

        config.MaxOutputTokens.Should().Be(4096);
        config.ReasoningMode.Should().Be("high");
        config.TimeoutSeconds.Should().Be(90);
        config.ActivePhases.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithNullActivePhases_DefaultsToEmpty()
    {
        var config = new AgentConfig(
            AgentId: Domain.Agents.AgentId.GeminiAgent,
            ModelProvider: "gemini",
            ModelName: "gemini-3.1-pro-preview",
            ActivePhases: null);

        config.ActivePhases.Should().NotBeNull();
        config.ActivePhases.Should().BeEmpty();
    }

    [Fact]
    public void DefaultCouncilConfigs_ContainsAllSixAgents()
    {
        var configs = AgentConfig.DefaultCouncil;

        configs.Should().HaveCount(6);
        configs.Select(c => c.AgentId).Should().BeEquivalentTo(AgentId.AllCouncil);
    }

    [Fact]
    public void DefaultCouncilConfigs_Moderator_UsesOpenAi()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.Moderator);

        config.ModelProvider.Should().Be("openai");
        config.ModelName.Should().Be("gpt-5.2");
        config.ActivePhases.Should().Contain(SessionPhase.Clarification);
    }

    [Fact]
    public void DefaultCouncilConfigs_GptAgent_UsesOpenAi()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.GptAgent);

        config.ModelProvider.Should().Be("openai");
        config.ModelName.Should().Be("gpt-5.2");
        config.ActivePhases.Should().Contain(SessionPhase.Construction);
        config.ActivePhases.Should().Contain(SessionPhase.Refinement);
        config.ActivePhases.Should().NotContain(SessionPhase.Critique);
    }

    [Fact]
    public void DefaultCouncilConfigs_GeminiAgent_UsesGemini()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.GeminiAgent);

        config.ModelProvider.Should().Be("gemini");
        config.ModelName.Should().Be("gemini-3.1-pro-preview");
        config.ActivePhases.Should().Contain(SessionPhase.Construction);
        config.ActivePhases.Should().Contain(SessionPhase.Refinement);
        config.ActivePhases.Should().NotContain(SessionPhase.Critique);
    }

    [Fact]
    public void DefaultCouncilConfigs_ClaudeAgent_UsesAnthropic()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.ClaudeAgent);

        config.ModelProvider.Should().Be("anthropic");
        config.ModelName.Should().Be("claude-opus-4-6");
        config.ActivePhases.Should().Contain(SessionPhase.Construction);
        config.ActivePhases.Should().Contain(SessionPhase.Refinement);
        config.ActivePhases.Should().NotContain(SessionPhase.Critique);
    }

    [Fact]
    public void DefaultCouncilConfigs_CritiqueAgent_UsesGemini()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.CritiqueAgent);

        config.ActivePhases.Should().ContainSingle()
            .Which.Should().Be(SessionPhase.Critique);
    }

    [Fact]
    public void DefaultCouncilConfigs_Synthesizer_UsesOpenAi()
    {
        var config = AgentConfig.DefaultCouncil
            .Single(c => c.AgentId == AgentId.Synthesizer);

        config.ModelProvider.Should().Be("openai");
        config.ModelName.Should().Be("gpt-5.2");
        config.ActivePhases.Should().Contain(SessionPhase.Synthesis);
        config.ActivePhases.Should().Contain(SessionPhase.TargetedLoop);
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
