using Agon.Domain.Sessions;
using Agon.Domain.TruthMap.Entities;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Application.Models;

/// <summary>
/// Everything an agent needs for a single call. Passed as a snapshot — immutable.
/// Constructed by AgentRunner before each dispatch.
/// </summary>
public sealed record AgentContext(
    Guid SessionId,
    TruthMapModel TruthMap,
    int FrictionLevel,
    SessionPhase Phase,
    int RoundNumber,
    /// <summary>
    /// The MESSAGEs of the agents this agent is assigned to critique (CRITIQUE phase only).
    /// Empty for all other phases.
    /// </summary>
    IReadOnlyList<AgentMessage> CritiqueTargetMessages,
    /// <summary>Top-K semantic memories relevant to this agent's role.</summary>
    IReadOnlyList<string> SemanticMemories,
    /// <summary>Specific directive injected for HITL micro-rounds or targeted loops.</summary>
    string? MicroDirective,
    /// <summary>Whether research tools are enabled for this session.</summary>
    bool ResearchToolsEnabled)
{
    public static AgentContext ForAnalysis(
        Guid sessionId,
        TruthMapModel truthMap,
        int frictionLevel,
        int roundNumber,
        bool researchToolsEnabled = false) =>
        new(
            sessionId,
            truthMap,
            frictionLevel,
            SessionPhase.AnalysisRound,
            roundNumber,
            [],
            [],
            null,
            researchToolsEnabled);

    public static AgentContext ForCritique(
        Guid sessionId,
        TruthMapModel truthMap,
        int frictionLevel,
        int roundNumber,
        IReadOnlyList<AgentMessage> critiqueTargetMessages,
        bool researchToolsEnabled = false) =>
        new(
            sessionId,
            truthMap,
            frictionLevel,
            SessionPhase.Critique,
            roundNumber,
            critiqueTargetMessages,
            [],
            null,
            researchToolsEnabled);

    public static AgentContext ForTargetedLoop(
        Guid sessionId,
        TruthMapModel truthMap,
        int frictionLevel,
        int roundNumber,
        string microDirective,
        bool researchToolsEnabled = false) =>
        new(
            sessionId,
            truthMap,
            frictionLevel,
            SessionPhase.TargetedLoop,
            roundNumber,
            [],
            [],
            microDirective,
            researchToolsEnabled);
}

/// <summary>A captured agent message from a prior round, used as critique input.</summary>
public sealed record AgentMessage(string AgentId, string Message);
