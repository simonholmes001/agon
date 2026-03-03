namespace Agon.Domain.Sessions;

/// <summary>
/// All session phases from the round-policy specification v2.0.
/// The Orchestrator transitions between these phases deterministically.
///
/// New parallel agent graph:
///   INTAKE → CLARIFICATION (GPT moderator, may loop) → CONSTRUCTION (GPT+Gemini+Claude in parallel)
///          → CRITIQUE (critique agent reviews all proposals) → REFINEMENT (agents refine, bounded)
///          → SYNTHESIS → DELIVER | DELIVER_WITH_GAPS → POST_DELIVERY
/// </summary>
public enum SessionPhase
{
    Intake,
    Clarification,      // GPT moderator asks clarifying questions (may loop up to MaxClarificationRounds)
    Construction,       // GPT, Gemini, Claude all run in parallel producing proposals
    Critique,           // Critique agent reviews all proposals and produces suggestions
    Refinement,         // GPT, Gemini, Claude all refine their proposals based on critique (bounded)
    Synthesis,          // Synthesizer produces final unified output from all refined proposals
    TargetedLoop,       // Reserved: additional targeted gap-filling after synthesis
    Deliver,
    DeliverWithGaps,
    PostDelivery
}
