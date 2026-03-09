using Agon.Domain.Agents;
using FluentAssertions;

namespace Agon.Domain.Tests.Agents;

/// <summary>
/// TDD tests to ensure agent prompts instruct agents to include required fields
/// that will pass PatchValidator validation.
/// </summary>
public class AgentPromptComplianceTests
{
    // ── Rule 4: Decisions require 'rationale' field ──────────────────────────

    [Theory]
    [InlineData(nameof(AgentSystemPrompts.GptAgent))]
    [InlineData(nameof(AgentSystemPrompts.GeminiAgent))]
    [InlineData(nameof(AgentSystemPrompts.ClaudeAgent))]
    [InlineData(nameof(AgentSystemPrompts.Synthesizer))]
    public void AnalysisPrompts_ShouldInstructAgents_ToIncludeRationaleInDecisions(string promptName)
    {
        // Get the prompt via reflection
        var prompt = GetPromptByName(promptName);

        // CRITICAL: Prompts must explicitly instruct agents to include 'rationale' when adding decisions
        // Otherwise agents will generate patches that fail PatchValidator Rule 4
        var lowerPrompt = prompt.ToLowerInvariant();
        
        // Check that the prompt mentions 'rationale'
        lowerPrompt.Should().Contain("rationale",
            "agents must know to include this required field for decisions");

        // More specific check: the prompt should mention rationale in the context of decisions
        var mentionsDecisions = lowerPrompt.Contains("decision");
        mentionsDecisions.Should().BeTrue(
            "the prompt must mention 'decision' entity type");
    }

    // ── Rule 5: Assumptions require 'validation_step' after Round 2 ──────────

    [Fact]
    public void SynthesizerPrompt_ShouldInstructAgent_ToAddValidationStepsToAssumptions()
    {
        var prompt = AgentSystemPrompts.Synthesizer;
        var lowerPrompt = prompt.ToLowerInvariant();

        // Synthesizer is explicitly responsible for ensuring all assumptions have validation steps
        lowerPrompt.Should().Contain("assumption",
            "Synthesizer must ensure all assumptions have validation_step field");
        
        lowerPrompt.Should().Contain("validation",
            "Synthesizer must reference validation concept");

        // Check for explicit instruction
        var mentionsValidationStep = lowerPrompt.Contains("validation_step") || lowerPrompt.Contains("validation step");
        mentionsValidationStep.Should().BeTrue(
            "Synthesizer prompt must explicitly mention validation_step requirement");
    }

    // ── Patch schema compliance ──────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(AgentSystemPrompts.GptAgent))]
    [InlineData(nameof(AgentSystemPrompts.GeminiAgent))]
    [InlineData(nameof(AgentSystemPrompts.ClaudeAgent))]
    [InlineData(nameof(AgentSystemPrompts.Synthesizer))]
    [InlineData(nameof(AgentSystemPrompts.CritiqueMode))]
    public void AllAgentPrompts_ShouldReferenceTruthMapPatchSchema(string promptName)
    {
        var prompt = GetPromptByName(promptName);

        // All prompts must reference the TruthMapPatch schema to ensure agents know the format
        prompt.Should().Contain("TruthMapPatch",
            "agents must be aware of the canonical patch format they must adhere to");
    }

    [Theory]
    [InlineData(nameof(AgentSystemPrompts.GptAgent))]
    [InlineData(nameof(AgentSystemPrompts.GeminiAgent))]
    [InlineData(nameof(AgentSystemPrompts.ClaudeAgent))]
    [InlineData(nameof(AgentSystemPrompts.Synthesizer))]
    [InlineData(nameof(AgentSystemPrompts.CritiqueMode))]
    public void AllAgentPrompts_ShouldIncludePatchRulesSection(string promptName)
    {
        var prompt = GetPromptByName(promptName);

        // All prompts must have a PATCH RULES section that explicitly states what can be added/modified
        var hasPatchRules = prompt.Contains("PATCH RULES") || prompt.Contains("PATCH:");
        hasPatchRules.Should().BeTrue(
            "agents must have clear guidance on what patch operations are permitted");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string GetPromptByName(string promptName)
    {
        var field = typeof(AgentSystemPrompts).GetField(promptName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        if (field is null)
            throw new ArgumentException($"Prompt '{promptName}' not found in AgentSystemPrompts");

        return (string)field.GetValue(null)!;
    }
}
