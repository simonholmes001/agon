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
        AgentSystemPrompts.GptAgentDraft.Should().Contain("Initial Analyst");
        AgentSystemPrompts.GptAgentDraft.Should().Contain("comprehensive analysis");
        AgentSystemPrompts.GptAgentDraft.Should().Contain("foundation");
    }

    [Fact]
    public void GeminiAgentImprove_PromptContainsRoleAndImprovement()
    {
        AgentSystemPrompts.GeminiAgentImprove.Should().Contain("Draft Improver");
        AgentSystemPrompts.GeminiAgentImprove.Should().Contain("Improve");
        AgentSystemPrompts.GeminiAgentImprove.Should().Contain("DELTA");
    }

    [Fact]
    public void ClaudeAgentRefine_PromptContainsRoleAndRefinement()
    {
        AgentSystemPrompts.ClaudeAgentRefine.Should().Contain("Draft Refiner");
        AgentSystemPrompts.ClaudeAgentRefine.Should().Contain("polished");
        AgentSystemPrompts.ClaudeAgentRefine.Should().Contain("harmonise");
    }

    [Fact]
    public void CritiqueMode_PromptContainsRoleAndCritique()
    {
        AgentSystemPrompts.CritiqueMode.Should().Contain("Critic");
        AgentSystemPrompts.CritiqueMode.Should().Contain("critique");
        AgentSystemPrompts.CritiqueMode.Should().Contain("weaknesses");
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
        AgentSystemPrompts.GptAgentDraft.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.GeminiAgentImprove.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.ClaudeAgentRefine.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.CritiqueMode.Should().NotBeNullOrWhiteSpace();
        AgentSystemPrompts.Synthesizer.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AllPrompts_ContainPatchRules()
    {
        AgentSystemPrompts.Moderator.Should().Contain("PATCH");
        AgentSystemPrompts.GptAgentDraft.Should().Contain("PATCH");
        AgentSystemPrompts.GeminiAgentImprove.Should().Contain("PATCH");
        AgentSystemPrompts.ClaudeAgentRefine.Should().Contain("PATCH");
        AgentSystemPrompts.CritiqueMode.Should().Contain("PATCH");
        AgentSystemPrompts.Synthesizer.Should().Contain("PATCH");
    }

    [Fact]
    public void GetPrompt_WithPhase_ReturnsCorrectPromptForAgentId()
    {
        AgentSystemPrompts.GetPrompt(AgentId.Moderator, SessionPhase.Clarification).Should().Be(AgentSystemPrompts.Moderator);
        AgentSystemPrompts.GetPrompt(AgentId.GptAgent, SessionPhase.DraftRound1).Should().Be(AgentSystemPrompts.GptAgentDraft);
        AgentSystemPrompts.GetPrompt(AgentId.GeminiAgent, SessionPhase.DraftRound2).Should().Be(AgentSystemPrompts.GeminiAgentImprove);
        AgentSystemPrompts.GetPrompt(AgentId.ClaudeAgent, SessionPhase.DraftRound3).Should().Be(AgentSystemPrompts.ClaudeAgentRefine);
        AgentSystemPrompts.GetPrompt(AgentId.Synthesizer, SessionPhase.Synthesis).Should().Be(AgentSystemPrompts.Synthesizer);
    }

    [Fact]
    public void GetPrompt_InCritiquePhase_ReturnsCritiquePromptWithModelName()
    {
        var gptCritique = AgentSystemPrompts.GetPrompt(AgentId.GptAgent, SessionPhase.Critique);
        var geminiCritique = AgentSystemPrompts.GetPrompt(AgentId.GeminiAgent, SessionPhase.Critique);
        var claudeCritique = AgentSystemPrompts.GetPrompt(AgentId.ClaudeAgent, SessionPhase.Critique);

        gptCritique.Should().Contain("GPT-5.2");
        geminiCritique.Should().Contain("Gemini 3");
        claudeCritique.Should().Contain("Claude Opus 4.6");
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
