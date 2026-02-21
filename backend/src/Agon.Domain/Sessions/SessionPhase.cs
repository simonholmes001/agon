namespace Agon.Domain.Sessions;

/// <summary>
/// All session phases from the round-policy specification.
/// The Orchestrator transitions between these phases deterministically.
/// </summary>
public enum SessionPhase
{
    Intake,
    Clarification,
    DebateRound1,
    DebateRound2,
    Synthesis,
    TargetedLoop,
    Deliver,
    DeliverWithGaps,
    PostDelivery
}
