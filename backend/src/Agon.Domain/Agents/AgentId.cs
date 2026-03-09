namespace Agon.Domain.Agents;

/// <summary>
/// Canonical agent identifiers used throughout the session.
/// These values must match the "agent" field written into TruthMapPatch meta
/// and entity provenance records.
/// </summary>
public static class AgentId
{
    public const string Moderator = "moderator";
    public const string GptAgent = "gpt_agent";
    public const string GeminiAgent = "gemini_agent";
    public const string ClaudeAgent = "claude_agent";
    public const string Synthesizer = "synthesizer";
    public const string PostDeliveryAssistant = "post_delivery_assistant";
    public const string ResearchLibrarian = "research_librarian";
    public const string Orchestrator = "orchestrator";
    public const string User = "user";

    /// <summary>
    /// The three council agents that run in parallel during Analysis and Critique rounds.
    /// Order is alphabetical — this is the deterministic patch-application order.
    /// </summary>
    public static readonly IReadOnlyList<string> CouncilAgents = new[]
    {
        ClaudeAgent,
        GeminiAgent,
        GptAgent
    };
}
