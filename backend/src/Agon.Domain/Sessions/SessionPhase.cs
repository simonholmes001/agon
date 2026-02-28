namespace Agon.Domain.Sessions;

/// <summary>
/// All session phases from the round-policy specification v2.0.
/// The Orchestrator transitions between these phases deterministically.
/// 
/// Sequential drafting flow:
///   INTAKE → CLARIFICATION → DRAFT_ROUND_1 (GPT) → DRAFT_ROUND_2 (Gemini) → DRAFT_ROUND_3 (Claude) → CRITIQUE → SYNTHESIS
/// </summary>
public enum SessionPhase
{
    Intake,
    Clarification,
    DraftRound1,    // GPT Agent creates initial draft
    DraftRound2,    // Gemini Agent improves draft
    DraftRound3,    // Claude Agent refines draft
    Critique,       // All three agents critique in parallel
    Synthesis,
    TargetedLoop,
    Deliver,
    DeliverWithGaps,
    PostDelivery
}
