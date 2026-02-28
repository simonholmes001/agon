using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;

namespace Agon.Application.Orchestration;

/// <summary>
/// State machine that controls session phase transitions in the simplified three-model architecture.
/// 
/// Flow: INTAKE → CLARIFICATION → DRAFT_ROUND_1 (GPT) → DRAFT_ROUND_2 (Gemini) → DRAFT_ROUND_3 (Claude)
///       → CRITIQUE (all three agents in parallel) → SYNTHESIS → [converged] DELIVER | [gaps] TARGETED_LOOP
/// </summary>
public class Orchestrator
{
    /// <summary>
    /// Transitions from Clarification to Draft Round 1 (GPT Agent).
    /// </summary>
    public SessionState StartDraftRound1(SessionState session)
    {
        if (session.Phase != SessionPhase.Clarification)
        {
            throw new InvalidOperationException($"Cannot start draft from phase '{session.Phase}'. Expected Clarification.");
        }

        session.Phase = SessionPhase.DraftRound1;
        session.RoundNumber = 1;
        return session;
    }

    /// <summary>
    /// Transitions from Draft Round 1 to Draft Round 2 (Gemini Agent).
    /// </summary>
    public SessionState TransitionToDraftRound2(SessionState session)
    {
        if (session.Phase != SessionPhase.DraftRound1)
        {
            throw new InvalidOperationException($"Cannot transition to DraftRound2 from phase '{session.Phase}'. Expected DraftRound1.");
        }

        session.Phase = SessionPhase.DraftRound2;
        session.RoundNumber = 2;
        return session;
    }

    /// <summary>
    /// Transitions from Draft Round 2 to Draft Round 3 (Claude Agent).
    /// </summary>
    public SessionState TransitionToDraftRound3(SessionState session)
    {
        if (session.Phase != SessionPhase.DraftRound2)
        {
            throw new InvalidOperationException($"Cannot transition to DraftRound3 from phase '{session.Phase}'. Expected DraftRound2.");
        }

        session.Phase = SessionPhase.DraftRound3;
        session.RoundNumber = 3;
        return session;
    }

    /// <summary>
    /// Transitions from Draft Round 3 to Critique phase (all agents critique in parallel).
    /// </summary>
    public SessionState TransitionToCritique(SessionState session)
    {
        if (session.Phase != SessionPhase.DraftRound3)
        {
            throw new InvalidOperationException($"Cannot transition to Critique from phase '{session.Phase}'. Expected DraftRound3.");
        }

        session.Phase = SessionPhase.Critique;
        session.RoundNumber = 4;
        return session;
    }

    /// <summary>
    /// Transitions from Critique to Synthesis phase.
    /// </summary>
    public SessionState TransitionToSynthesis(SessionState session)
    {
        if (session.Phase != SessionPhase.Critique && session.Phase != SessionPhase.TargetedLoop)
        {
            throw new InvalidOperationException($"Cannot transition to Synthesis from phase '{session.Phase}'. Expected Critique or TargetedLoop.");
        }

        session.Phase = SessionPhase.Synthesis;
        return session;
    }

    /// <summary>
    /// Evaluates the synthesis result and transitions to the appropriate next phase:
    /// - DELIVER if converged and no blocking questions
    /// - TARGETED_LOOP if not converged but loop budget remains
    /// - DELIVER_WITH_GAPS if loop budget exhausted
    /// </summary>
    public SessionState TransitionFromSynthesis(SessionState session, TruthMapState map)
    {
        if (session.Phase != SessionPhase.Synthesis)
        {
            throw new InvalidOperationException($"Synthesis transition requires phase Synthesis, got '{session.Phase}'.");
        }

        ConvergenceEvaluator.Evaluate(map.Convergence, session.FrictionLevel, session.RoundPolicy);
        var hasBlockingQuestions = map.OpenQuestions.Any(question => question.Blocking);
        var converged = map.Convergence.Status == Domain.TruthMap.Entities.ConvergenceStatus.Converged;

        if (converged && !hasBlockingQuestions)
        {
            session.Phase = SessionPhase.Deliver;
            session.Status = SessionStatus.Complete;
            return session;
        }

        if (!session.RoundPolicy.ShouldTerminateTargetedLoop(session.TargetedLoopCount))
        {
            session.TargetedLoopCount++;
            session.Phase = SessionPhase.TargetedLoop;
            session.Status = SessionStatus.Active;
            return session;
        }

        session.Phase = SessionPhase.DeliverWithGaps;
        session.Status = SessionStatus.CompleteWithGaps;
        return session;
    }

    /// <summary>
    /// Gets the next phase in the sequential drafting flow.
    /// </summary>
    public static SessionPhase GetNextPhase(SessionPhase currentPhase) => currentPhase switch
    {
        SessionPhase.Intake => SessionPhase.Clarification,
        SessionPhase.Clarification => SessionPhase.DraftRound1,
        SessionPhase.DraftRound1 => SessionPhase.DraftRound2,
        SessionPhase.DraftRound2 => SessionPhase.DraftRound3,
        SessionPhase.DraftRound3 => SessionPhase.Critique,
        SessionPhase.Critique => SessionPhase.Synthesis,
        SessionPhase.TargetedLoop => SessionPhase.Synthesis,
        _ => throw new InvalidOperationException($"No automatic next phase from '{currentPhase}'.")
    };
}
