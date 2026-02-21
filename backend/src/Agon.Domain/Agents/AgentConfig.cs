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
    /// Default council configuration matching the agent roster
    /// in architecture.instructions.md §4.3.
    /// </summary>
    public static IReadOnlyList<AgentConfig> DefaultCouncil { get; } =
    [
        new(Agents.AgentId.SocraticClarifier,
            ModelProvider: "openai",
            ModelName: "gpt-5.2-thinking",
            MaxOutputTokens: 4096,
            ActivePhases: [SessionPhase.Clarification]),

        new(Agents.AgentId.FramingChallenger,
            ModelProvider: "gemini",
            ModelName: "gemini-3",
            MaxOutputTokens: 4096,
            ActivePhases: [SessionPhase.DebateRound1]),

        new(Agents.AgentId.ProductStrategist,
            ModelProvider: "anthropic",
            ModelName: "claude-opus-4.6",
            MaxOutputTokens: 4096,
            ActivePhases: [SessionPhase.DebateRound1, SessionPhase.DebateRound2]),

        new(Agents.AgentId.TechnicalArchitect,
            ModelProvider: "openai",
            ModelName: "gpt-5.2-thinking",
            MaxOutputTokens: 4096,
            ActivePhases: [SessionPhase.DebateRound1, SessionPhase.DebateRound2]),

        new(Agents.AgentId.Contrarian,
            ModelProvider: "gemini",
            ModelName: "gemini-3",
            MaxOutputTokens: 4096,
            ActivePhases: [SessionPhase.DebateRound1, SessionPhase.DebateRound2]),

        new(Agents.AgentId.ResearchLibrarian,
            ModelProvider: "openai",
            ModelName: "gpt-5.2-thinking",
            MaxOutputTokens: 4096,
            ActivePhases: [SessionPhase.DebateRound1]),

        new(Agents.AgentId.SynthesisValidation,
            ModelProvider: "openai",
            ModelName: "gpt-5.2-thinking",
            MaxOutputTokens: 8192,
            ActivePhases: [SessionPhase.Synthesis, SessionPhase.TargetedLoop])
    ];
}
