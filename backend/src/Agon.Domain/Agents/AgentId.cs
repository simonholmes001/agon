namespace Agon.Domain.Agents;

/// <summary>
/// Constants for all agent identifiers used throughout the system.
/// These are the canonical agent IDs referenced in patches, prompts, and orchestration.
/// </summary>
public static class AgentId
{
    // Intake / Clarification
    public const string Moderator = "moderator";
    
    // Working agents (one per model provider) — run in parallel during Construction + Refinement
    public const string GptAgent = "gpt_agent";
    public const string GeminiAgent = "gemini_agent";
    public const string ClaudeAgent = "claude_agent";

    // Critique agent — reviews all parallel proposals and produces structured feedback
    public const string CritiqueAgent = "critique_agent";
    
    // Synthesis
    public const string Synthesizer = "synthesizer";

    // System roles
    public const string Orchestrator = "orchestrator";
    public const string User = "user";

    private static readonly HashSet<string> CouncilAgents = new(StringComparer.Ordinal)
    {
        Moderator,
        GptAgent,
        GeminiAgent,
        ClaudeAgent,
        CritiqueAgent,
        Synthesizer
    };

    /// <summary>
    /// Working agents that participate in Construction and Refinement phases (run in parallel).
    /// </summary>
    public static IReadOnlyList<string> WorkingAgents { get; } = new[] { GptAgent, GeminiAgent, ClaudeAgent };

    public static IReadOnlyList<string> AllCouncil { get; } = CouncilAgents.ToList().AsReadOnly();

    public static bool IsCouncilAgent(string agentId) => CouncilAgents.Contains(agentId);
}
