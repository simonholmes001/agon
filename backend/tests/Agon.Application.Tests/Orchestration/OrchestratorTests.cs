using Agon.Application.Orchestration;
using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using FluentAssertions;

namespace Agon.Application.Tests.Orchestration;

public class OrchestratorTests
{
    [Fact]
    public void TransitionFromSynthesis_GoesToDeliver_WhenConvergedWithoutBlockingQuestions()
    {
        var sut = new Orchestrator();
        var session = CreateSession(phase: SessionPhase.Synthesis, targetedLoopCount: 0, friction: 50);
        var map = TruthMapState.CreateNew(session.SessionId);
        map.Convergence.ClaritySpecificity = 0.9f;
        map.Convergence.Feasibility = 0.9f;
        map.Convergence.RiskCoverage = 0.9f;
        map.Convergence.AssumptionExplicitness = 0.9f;
        map.Convergence.Coherence = 0.9f;
        map.Convergence.Actionability = 0.9f;
        map.Convergence.EvidenceQuality = 0.9f;

        var updated = sut.TransitionFromSynthesis(session, map);

        updated.Phase.Should().Be(SessionPhase.Deliver);
        updated.Status.Should().Be(SessionStatus.Complete);
    }

    [Fact]
    public void TransitionFromSynthesis_GoesToTargetedLoop_WhenNotConvergedAndLoopsRemain()
    {
        var sut = new Orchestrator();
        var session = CreateSession(phase: SessionPhase.Synthesis, targetedLoopCount: 1, friction: 50);
        var map = TruthMapState.CreateNew(session.SessionId);

        var updated = sut.TransitionFromSynthesis(session, map);

        updated.Phase.Should().Be(SessionPhase.TargetedLoop);
        updated.TargetedLoopCount.Should().Be(2);
        updated.Status.Should().Be(SessionStatus.Active);
    }

    [Fact]
    public void TransitionFromSynthesis_GoesToDeliverWithGaps_WhenLoopsExhausted()
    {
        var sut = new Orchestrator();
        var session = CreateSession(phase: SessionPhase.Synthesis, targetedLoopCount: 2, friction: 50);
        session.RoundPolicy = new RoundPolicy { MaxTargetedLoops = 2 };
        var map = TruthMapState.CreateNew(session.SessionId);

        var updated = sut.TransitionFromSynthesis(session, map);

        updated.Phase.Should().Be(SessionPhase.DeliverWithGaps);
        updated.Status.Should().Be(SessionStatus.CompleteWithGaps);
    }

    private static SessionState CreateSession(SessionPhase phase, int targetedLoopCount, int friction)
    {
        return new SessionState
        {
            SessionId = Guid.NewGuid(),
            Phase = phase,
            Status = SessionStatus.Active,
            Mode = SessionMode.Deep,
            FrictionLevel = friction,
            RoundPolicy = new RoundPolicy(),
            TargetedLoopCount = targetedLoopCount
        };
    }
}
