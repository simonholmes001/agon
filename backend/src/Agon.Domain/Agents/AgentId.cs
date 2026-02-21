namespace Agon.Domain.Agents;

/// <summary>
/// Constants for all agent identifiers used throughout the system.
/// These are the canonical agent IDs referenced in patches, prompts, and orchestration.
/// </summary>
public static class AgentId
{
    public const string SocraticClarifier = "socratic_clarifier";
    public const string FramingChallenger = "framing_challenger";
    public const string ProductStrategist = "product_strategist";
    public const string TechnicalArchitect = "technical_architect";
    public const string Contrarian = "contrarian";
    public const string ResearchLibrarian = "research_librarian";
    public const string SynthesisValidation = "synthesis_validation";

    public const string Orchestrator = "orchestrator";
    public const string User = "user";

    private static readonly HashSet<string> CouncilAgents = new(StringComparer.Ordinal)
    {
        SocraticClarifier,
        FramingChallenger,
        ProductStrategist,
        TechnicalArchitect,
        Contrarian,
        ResearchLibrarian,
        SynthesisValidation
    };

    public static IReadOnlyList<string> AllCouncil { get; } = CouncilAgents.ToList().AsReadOnly();

    public static bool IsCouncilAgent(string agentId) => CouncilAgents.Contains(agentId);
}
