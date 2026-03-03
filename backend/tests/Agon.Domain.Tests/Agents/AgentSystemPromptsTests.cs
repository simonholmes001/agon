using Agon.Domain.Agents;
using Agon.Domain.Sessions;
using FluentAssertions;

namespace Agon.Domain.Tests.Agents;

public class AgentSystemPromptsTests
{
    [Fact]
    public void Moderator_PromptContainsRoleAndGoldenTriangle()
    {
        AgentSystemPrompts.Moderator.Should().Contain("Moderator");
        AgentSystemPrompts.Moderator.Should().Contain("Golden Triangle");
        AgentSystemPrompts.Moderator.Should().Contain("Debate Brief");
    }

    [Fact]
    public void GptAgentDraft_PromptContainsRoleAndAnalysis()
    {
        AgentSystemPrompts.GptAgentConstruct.Should().Contain("Constructor");
        AgentSystemPrompts.GptAgentConstruct.Should().Contain("proposal");
        AgentSystemPrompts.GptAgentConstruct.Should().Contain("GPT");
    }

    [Fact]
    public void GeminiAgentImprove_PromptContainsRoleAndImprovement()
    {
        AgentSystemPrompts.GeminiAgentConstruct.Should().Contain("Constructor");
        AgentSystemPrompts.GeminiAgentConstruct.Should().Contain("proposal");
        AgentSystemPrompts.GeminiAgentConstruct.Should().Contain("Gemini");
    }

    [Fact]
    public void ClaudeAgentRefine_PromptContainsRoleAndRefinement()
    {
        AgentSystemPrompts.ClaudeAgentRefine.Should().Contain("Refiner");
        AgentSystemPrompts.ClaudeAgentRefine.Should().Contain("critique");
        AgentSystemPrompts.ClaudeAgentRefine.Should().Contain("Claude Opus");
    }

    [Fact]
    public void CritiqueMode_PromptContainsRoleAndCritique()
    {
        AgentSystemPrompts.CritiqueAgentPrompt.Should().Contain("Critique Agent");
        AgentSystemPrompts.CritiqueAgentPrompt.Should().Contain("CRITIQUE SUMMARY");
        AgentSystemPrompts.CritiqueAgentPrompt.Should().Contain("feedback");
    }

    [Fact]
    public void Synthesizer_PromptContainsRoleAndConvergence()
    {
        AgentSystemPrompts.Synthesizer.Should().Contain("Synthesizer");
        AgentSystemPrompts.Synthesizer.Should().Contain("convergence");
    }

    [Fact]
    public void AllPrompts_AreNonEmptyStrings()
    {
        AgentSystemPrompts.Moderator.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.GptAgentConstruct.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.GptAgentRefine.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.GeminiAgentConstruct.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.GeminiAgentRefine.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.ClaudeAgentConstruct.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.ClaudeAgentRefine.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.CritiqueAgentPrompt.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.Synthesizer.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AllPrompts_ContainPatchRules()
    {
        AgentSystemPrompts.Moderator.Should().Contain("PATCH");
        AgentSystemPrompts.GptAgentConstruct.Should().Contain("PATCH");
        AgentSystemPrompts.GeminiAgentConstruct.Should().Contain("PATCH");
        AgentSystemPrompts.ClaudeAgentConstruct.Should().Contain("PATCH");
        AgentSystemPrompts.CritiqueAgentPrompt.Should().Contain("PATCH");
        AgentSystemPrompts.Synthesizer.Should().Contain("PATCH");
    }

    [Fact]
    public void GetPrompt_WithPhase_ReturnsCorrectPromptForAgentId()
    {
        AgentSystemPrompts.GetPrompt(AgentId.Moderator, SessionPhase.Clarification).Should().Be(AgentSystemPrompts.Moderator);
        AgentSystemPrompts.GetPrompt(AgentId.GptAgent, SessionPhase.Construction).Should().Be(AgentSystemPrompts.GptAgentConstruct);
        AgentSystemPrompts.GetPrompt(AgentId.GptAgent, SessionPhase.Refinement).Should().Be(AgentSystemPrompts.GptAgentRefine);
        AgentSystemPrompts.GetPrompt(AgentId.GeminiAgent, SessionPhase.Construction).Should().Be(AgentSystemPrompts.GeminiAgentConstruct);
        AgentSystemPrompts.GetPrompt(AgentId.GeminiAgent, SessionPhase.Refinement).Should().Be(AgentSystemPrompts.GeminiAgentRefine);
        AgentSystemPrompts.GetPrompt(AgentId.ClaudeAgent, SessionPhase.Construction).Should().Be(AgentSystemPrompts.ClaudeAgentConstruct);
        AgentSystemPrompts.GetPrompt(AgentId.ClaudeAgent, SessionPhase.Refinement).Should().Be(AgentSystemPrompts.ClaudeAgentRefine);
        AgentSystemPrompts.GetPrompt(AgentId.CritiqueAgent, SessionPhase.Critique).Should().Be(AgentSystemPrompts.CritiqueAgentPrompt);
        AgentSystemPrompts.GetPrompt(AgentId.Synthesizer, SessionPhase.Synthesis).Should().Be(AgentSystemPrompts.Synthesizer);
    }

    [Fact]
    public void GetPrompt_InCritiquePhase_ReturnsCritiquePromptWithModelName()
    {
        var critiquePrompt = AgentSystemPrompts.GetPrompt(AgentId.CritiqueAgent, SessionPhase.Critique);

        critiquePrompt.Should().Contain("Critique");
        critiquePrompt.Should().Be(AgentSystemPrompts.CritiqueAgentPrompt);
    }

    [Fact]
    public void GetPrompt_ReturnsCorrectPromptForAgentId()
    {
        AgentSystemPrompts.GetPrompt(AgentId.Moderator).Should().Be(AgentSystemPrompts.Moderator);
        AgentSystemPrompts.GetPrompt(AgentId.Synthesizer).Should().Be(AgentSystemPrompts.Synthesizer);
    }

    [Fact]
    public void GetPrompt_ThrowsForUnknownAgentId()
    {
        var act = () => AgentSystemPrompts.GetPrompt("unknown_agent");

        act.Should().Throw<ArgumentException>();
    }
}
