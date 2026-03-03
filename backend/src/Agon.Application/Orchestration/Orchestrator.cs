using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;

namespace Agon.Application.Orchestration;

/// <summary>
/// State machine that controls session phase transitions in the parallel-construction architecture.
///
/// Flow:
///   INTAKE → CLARIFICATION (Moderator, may loop up to MaxClarificationRounds)
///          → CONSTRUCTION  (GPT + Gemini + Claude in parallel)
///          → CRITIQUE      (CritiqueAgent reviews all proposals)
///          → REFINEMENT    (GPT + Gemini + Claude in parallel, bounded by MaxRefinementIterations)
///              ↑______|   (loops back to CRITIQUE until iterations exhausted)
///          → SYNTHESIS
///          → DELIVER | DELIVER_WITH_GAPS
///          → POST_DELIVERY
/// </summary>
public class Orchestrator
{
    // ── Clarification ──────────────────────────────────────────────────────────

    /// <summary>
    /// Transitions from Intake to Clarification. The Moderator agent runs here.
    /// </summary>
    public SessionState StartClarification(SessionState session)
    {
        if (session.Phase != SessionPhase.Intake && session.Phase != SessionPhase.Clarification)
        {
            throw new InvalidOperationException(
                $"Cannot start clarification from phase '{session.Phase}'. Expected Intake or Clarification.");
        }

        session.Phase = SessionPhase.Clarification;
        session.RoundNumber = 0;
        return session;
    }

    /// <summary>
    /// Increments the clarification round counter. Returns true if another round is allowed.
    /// </summary>
    public bool TryAdvanceClarificationRound(SessionState session)
    {
        session.ClarificationRoundCount++;
        return !session.RoundPolicy.ShouldTerminateClarification(session.ClarificationRoundCount);
    }

    // ── Construction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Transitions from Clarification to Construction.
    /// GPT, Gemini, and Claude will run in parallel in this phase.
    /// </summary>
    public SessionState TransitionToConstruction(SessionState session)
    {
        if (session.Phase != SessionPhase.Clarification)
        {
            throw new InvalidOperationException(
                $"Cannot transition to Construction from phase '{session.Phase}'. Expected Clarification.");
        }

        session.Phase = SessionPhase.Construction;
        session.RoundNumber = 1;
        return session;
    }

    // ── Critique ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Transitions from Construction or Refinement to Critique.
    /// </summary>
    public SessionState TransitionToCritique(SessionState session)
    {
        if (session.Phase != SessionPhase.Construction && session.Phase != SessionPhase.Refinement)
        {
            throw new InvalidOperationException(
                $"Cannot transition to Critique from phase '{session.Phase}'. Expected Construction or Refinement.");
        }

        session.Phase = SessionPhase.Critique;
        return session;
    }

    // ── Refinement ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Transitions from Critique to Refinement.
    /// GPT, Gemini, and Claude will run in parallel again with critique context.
    /// </summary>
    public SessionState TransitionToRefinement(SessionState session)
    {
        if (session.Phase != SessionPhase.Critique)
        {
            throw new InvalidOperationException(
                $"Cannot transition to Refinement from phase '{session.Phase}'. Expected Critique.");
        }

        session.Phase = SessionPhase.Refinement;
        session.RefinementIterationCount++;
        return session;
    }

    /// <summary>
    /// Returns true if another Critique→Refinement loop is allowed.
    /// </summary>
    public bool ShouldContinueRefinement(SessionState session) =>
        !session.RoundPolicy.ShouldTerminateRefinement(session.RefinementIterationCount);

    // ── Synthesis ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Transitions from Critique, Refinement, or TargetedLoop to Synthesis.
    /// </summary>
    public SessionState TransitionToSynthesis(SessionState session)
    {
        if (session.Phase != SessionPhase.Critique
            && session.Phase != SessionPhase.Refinement
            && session.Phase != SessionPhase.TargetedLoop)
        {
            throw new InvalidOperationException(
                $"Cannot transition to Synthesis from phase '{session.Phase}'.");
        }

        session.Phase = SessionPhase.Synthesis;
        return session;
    }

    // ── Deliver ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates the synthesis result and transitions to Deliver, TargetedLoop, or DeliverWithGaps.
    /// </summary>
    public SessionState TransitionFromSynthesis(SessionState session, TruthMapState map)
    {
        if (session.Phase != SessionPhase.Synthesis)
        {
            throw new InvalidOperationException(
                $"Synthesis transition requires phase Synthesis, got '{session.Phase}'.");
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

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the next phase in the sequential flow (for simple one-step advance calls).
    /// </summary>
    public static SessionPhase GetNextPhase(SessionPhase currentPhase) => currentPhase switch
    {
        SessionPhase.Intake          => SessionPhase.Clarification,
        SessionPhase.Clarification   => SessionPhase.Construction,
        SessionPhase.Construction    => SessionPhase.Critique,
        SessionPhase.Critique        => SessionPhase.Refinement,
        SessionPhase.Refinement      => SessionPhase.Synthesis,
        SessionPhase.TargetedLoop    => SessionPhase.Synthesis,
        _ => throw new InvalidOperationException($"No automatic next phase from '{currentPhase}'.")
    };
}
