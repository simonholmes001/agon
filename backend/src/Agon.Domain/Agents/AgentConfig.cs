using Agon.Domain.Sessions;

namespace Agon.Domain.Agents;

/// <summary>
/// Per-agent configuration: model provider, token limits, timeout, and active phases.
/// This is a Domain-layer value object defining the shape of the config.
/// At runtime, values are bound from appsettings.json in the Infrastructure/Api layer.
/// </summary>
public sealed record AgentConfig
{
    public string AgentId { get; init; }
    public string ModelProvider { get; init; }
    public string ModelName { get; init; }
    public int MaxOutputTokens { get; init; } = 4096;
    public string ReasoningMode { get; init; } = "high";
    public int TimeoutSeconds { get; init; } = 90;
    public IReadOnlyList<SessionPhase> ActivePhases { get; init; } = Array.Empty<SessionPhase>();

    public AgentConfig(
        string AgentId,
        string ModelProvider,
        string ModelName,
        int MaxOutputTokens = 4096,
        string ReasoningMode = "high",
        int TimeoutSeconds = 90,
        IReadOnlyList<SessionPhase>? ActivePhases = null)
    {
        this.AgentId = AgentId;
        this.ModelProvider = ModelProvider;
        this.ModelName = ModelName;
        this.MaxOutputTokens = MaxOutputTokens;
        this.ReasoningMode = ReasoningMode;
        this.TimeoutSeconds = TimeoutSeconds;
        this.ActivePhases = ActivePhases ?? Array.Empty<SessionPhase>();
    }

    /// <summary>
    /// Default council configuration matching the simplified three-model architecture.
    /// 
    /// Workflow:
    /// 1. Moderator (GPT-5.2) handles clarification
    /// 2. GPT Agent (GPT-5.2) creates initial draft
    /// 3. Gemini Agent (Gemini 3) improves draft
    /// 4. Claude Agent (Claude Opus 4.6) refines draft
    /// 5. All three critique in parallel
    /// 6. Synthesizer (GPT-5.2) produces final report
    /// </summary>
    public static IReadOnlyList<AgentConfig> DefaultCouncil { get; } =
    [
        new(Agents.AgentId.Moderator,
            ModelProvider: "openai",
            ModelName: "gpt-5.2",
            MaxOutputTokens: 4096,
            ActivePhases: [SessionPhase.Clarification]),

        new(Agents.AgentId.GptAgent,
            ModelProvider: "openai",
            ModelName: "gpt-5.2",
            MaxOutputTokens: 8192,
            ActivePhases: [SessionPhase.DraftRound1, SessionPhase.Critique]),

        new(Agents.AgentId.GeminiAgent,
            ModelProvider: "gemini",
            ModelName: "gemini-3.1-pro-preview",
            MaxOutputTokens: 8192,
            ActivePhases: [SessionPhase.DraftRound2, SessionPhase.Critique]),

        new(Agents.AgentId.ClaudeAgent,
            ModelProvider: "anthropic",
            ModelName: "claude-opus-4-6",
            MaxOutputTokens: 8192,
            ActivePhases: [SessionPhase.DraftRound3, SessionPhase.Critique]),

        new(Agents.AgentId.Synthesizer,
            ModelProvider: "openai",
            ModelName: "gpt-5.2",
            MaxOutputTokens: 8192,
            ActivePhases: [SessionPhase.Synthesis, SessionPhase.TargetedLoop])
    ];
}
