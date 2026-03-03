using Agon.Domain.Sessions;

namespace Agon.Application.Sessions;

public class SessionState
{
    public Guid SessionId { get; init; }
    public SessionMode Mode { get; init; }
    public SessionStatus Status { get; set; }
    public SessionPhase Phase { get; set; }
    public int FrictionLevel { get; init; }
    public RoundPolicy RoundPolicy { get; set; } = new();
    public int RoundNumber { get; set; }
    public int TargetedLoopCount { get; set; }
    public int TokensUsed { get; set; }
    public bool ClarificationIncomplete { get; set; }

    /// <summary>
    /// How many clarification rounds have completed. Bounded by RoundPolicy.MaxClarificationRounds.
    /// </summary>
    public int ClarificationRoundCount { get; set; }

    /// <summary>
    /// How many Critique→Refinement iterations have completed. Bounded by RoundPolicy.MaxRefinementIterations.
    /// </summary>
    public int RefinementIterationCount { get; set; }

    /// <summary>
    /// The critique agent's last MESSAGE, injected as context into refinement rounds.
    /// </summary>
    public string? LastCritiqueMessage { get; set; }
}
