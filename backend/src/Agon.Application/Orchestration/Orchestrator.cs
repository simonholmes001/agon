using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;

namespace Agon.Application.Orchestration;

public class Orchestrator
{
    public SessionState StartDebateRound1(SessionState session)
    {
        if (session.Phase != SessionPhase.Clarification)
        {
            throw new InvalidOperationException($"Cannot start debate from phase '{session.Phase}'.");
        }

        session.Phase = SessionPhase.DebateRound1;
        session.RoundNumber = Math.Max(session.RoundNumber, 1);
        return session;
    }

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
}
