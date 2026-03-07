namespace Agon.Domain.Sessions;

/// <summary>
/// Ordered phases of an Agon session state machine.
/// The Orchestrator is the ONLY component that may advance or re-enter a phase.
/// LLM outputs cannot trigger phase transitions.
/// </summary>
public enum SessionPhase
{
    /// <summary>User has submitted an idea. Session and empty Truth Map are being initialised.</summary>
    Intake,

    /// <summary>
    /// Moderator is clarifying the idea with the user.
    /// All council agents are BLOCKED until READY is signalled.
    /// </summary>
    Clarification,

    /// <summary>
    /// All three council agents analyse the Debate Brief in parallel.
    /// They do NOT see each other's output during this phase.
    /// </summary>
    AnalysisRound,

    /// <summary>
    /// All three council agents critique the merged Analysis Round Truth Map in parallel.
    /// Each agent critiques the other two only — never its own output.
    /// </summary>
    Critique,

    /// <summary>
    /// Single-agent synthesis pass: coherent narrative, decisions, plan, and convergence scoring.
    /// </summary>
    Synthesis,

    /// <summary>
    /// A targeted sub-round dispatched to specific agents to address identified convergence gaps.
    /// May re-enter SYNTHESIS after completion.
    /// </summary>
    TargetedLoop,

    /// <summary>All convergence thresholds met. Artifact generation triggered.</summary>
    Deliver,

    /// <summary>
    /// Max targeted loops exhausted before convergence. Artifacts delivered with gaps noted.
    /// </summary>
    DeliverWithGaps,

    /// <summary>
    /// Session is complete; user may continue asking questions and challenging claims.
    /// No proactive agent calls — agents respond to user-initiated actions only.
    /// </summary>
    PostDelivery
}
