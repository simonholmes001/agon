using Agon.Domain.Sessions;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Application.Models;

/// <summary>
/// Runtime state of a session as maintained by the Orchestrator.
/// Mutable within a session's lifetime — held in Redis ephemeral state
/// and persisted via ISessionRepository at phase boundaries.
/// </summary>
public sealed class SessionState
{
    public Guid SessionId { get; init; }
    public Guid UserId { get; init; }
    public string? Idea { get; init; }
    public SessionMode Mode { get; init; }
    public SessionPhase Phase { get; set; }
    public SessionStatus Status { get; set; }
    public int CurrentRound { get; set; }
    public int TargetedLoopCount { get; set; }
    public int ClarificationRoundCount { get; set; }
    public int TokensUsed { get; set; }
    public int FrictionLevel { get; init; }
    public bool ResearchToolsEnabled { get; init; }
    public bool ClarificationIncomplete { get; set; }
    public TruthMapModel TruthMap { get; set; } = default!;
    public DebateBrief? DebateBrief { get; set; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Messages from the most recent analysis round, keyed by agent ID.</summary>
    public Dictionary<string, string> LastRoundMessages { get; } = new();

    /// <summary>User messages submitted during clarification (responses to Moderator questions).</summary>
    public List<UserMessage> UserMessages { get; } = new();

    public static SessionState Create(
        Guid sessionId,
        Guid userId,
        string idea,
        int frictionLevel,
        bool researchToolsEnabled,
        TruthMapModel initialTruthMap) =>
        new()
        {
            SessionId = sessionId,
            UserId = userId,
            Idea = idea,
            Mode = frictionLevel >= 70 ? SessionMode.Deep : SessionMode.Quick,
            Phase = SessionPhase.Intake,
            Status = SessionStatus.Active,
            CurrentRound = 0,
            TargetedLoopCount = 0,
            ClarificationRoundCount = 0,
            TokensUsed = 0,
            FrictionLevel = frictionLevel,
            ResearchToolsEnabled = researchToolsEnabled,
            TruthMap = initialTruthMap,
            CreatedAt = DateTimeOffset.UtcNow
        };

    // Legacy factory method for backward compatibility
    public static SessionState Create(
        Guid sessionId,
        int frictionLevel,
        bool researchToolsEnabled,
        TruthMapModel initialTruthMap) =>
        Create(sessionId, Guid.Empty, string.Empty, frictionLevel, researchToolsEnabled, initialTruthMap);
}
