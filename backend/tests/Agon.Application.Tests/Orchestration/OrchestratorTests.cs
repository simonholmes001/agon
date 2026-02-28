using Agon.Application.Orchestration;
using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Application.Tests.Orchestration;

public class OrchestratorTests
{
    private readonly Orchestrator _sut = new();

    #region StartDraftRound1 Tests

    [Fact]
    public void StartDraftRound1_TransitionsToDraftRound1_FromClarification()
    {
        var session = CreateSession(phase: SessionPhase.Clarification);

        var updated = _sut.StartDraftRound1(session);

        updated.Phase.Should().Be(SessionPhase.DraftRound1);
        updated.RoundNumber.Should().Be(1);
    }

    [Theory]
    [InlineData(SessionPhase.Intake)]
    [InlineData(SessionPhase.DraftRound1)]
    [InlineData(SessionPhase.DraftRound2)]
    [InlineData(SessionPhase.Synthesis)]
    [InlineData(SessionPhase.PostDelivery)]
    public void StartDraftRound1_ThrowsInvalidOperationException_FromInvalidPhase(SessionPhase invalidPhase)
    {
        var session = CreateSession(phase: invalidPhase);

        var act = () => _sut.StartDraftRound1(session);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{invalidPhase}*");
    }

    #endregion

    #region TransitionToDraftRound2 Tests

    [Fact]
    public void TransitionToDraftRound2_TransitionsToDraftRound2_FromDraftRound1()
    {
        var session = CreateSession(phase: SessionPhase.DraftRound1);

        var updated = _sut.TransitionToDraftRound2(session);

        updated.Phase.Should().Be(SessionPhase.DraftRound2);
        updated.RoundNumber.Should().Be(2);
    }

    [Theory]
    [InlineData(SessionPhase.Clarification)]
    [InlineData(SessionPhase.DraftRound2)]
    [InlineData(SessionPhase.DraftRound3)]
    [InlineData(SessionPhase.Critique)]
    public void TransitionToDraftRound2_ThrowsInvalidOperationException_FromInvalidPhase(SessionPhase invalidPhase)
    {
        var session = CreateSession(phase: invalidPhase);

        var act = () => _sut.TransitionToDraftRound2(session);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{invalidPhase}*");
    }

    #endregion

    #region TransitionToDraftRound3 Tests

    [Fact]
    public void TransitionToDraftRound3_TransitionsToDraftRound3_FromDraftRound2()
    {
        var session = CreateSession(phase: SessionPhase.DraftRound2);

        var updated = _sut.TransitionToDraftRound3(session);

        updated.Phase.Should().Be(SessionPhase.DraftRound3);
        updated.RoundNumber.Should().Be(3);
    }

    [Theory]
    [InlineData(SessionPhase.DraftRound1)]
    [InlineData(SessionPhase.DraftRound3)]
    [InlineData(SessionPhase.Critique)]
    [InlineData(SessionPhase.Synthesis)]
    public void TransitionToDraftRound3_ThrowsInvalidOperationException_FromInvalidPhase(SessionPhase invalidPhase)
    {
        var session = CreateSession(phase: invalidPhase);

        var act = () => _sut.TransitionToDraftRound3(session);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{invalidPhase}*");
    }

    #endregion

    #region TransitionToCritique Tests

    [Fact]
    public void TransitionToCritique_TransitionsToCritique_FromDraftRound3()
    {
        var session = CreateSession(phase: SessionPhase.DraftRound3);

        var updated = _sut.TransitionToCritique(session);

        updated.Phase.Should().Be(SessionPhase.Critique);
        updated.RoundNumber.Should().Be(4);
    }

    [Theory]
    [InlineData(SessionPhase.DraftRound2)]
    [InlineData(SessionPhase.Critique)]
    [InlineData(SessionPhase.Synthesis)]
    [InlineData(SessionPhase.Deliver)]
    public void TransitionToCritique_ThrowsInvalidOperationException_FromInvalidPhase(SessionPhase invalidPhase)
    {
        var session = CreateSession(phase: invalidPhase);

        var act = () => _sut.TransitionToCritique(session);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{invalidPhase}*");
    }

    #endregion

    #region TransitionToSynthesis Tests

    [Fact]
    public void TransitionToSynthesis_TransitionsToSynthesis_FromCritique()
    {
        var session = CreateSession(phase: SessionPhase.Critique);

        var updated = _sut.TransitionToSynthesis(session);

        updated.Phase.Should().Be(SessionPhase.Synthesis);
    }

    [Fact]
    public void TransitionToSynthesis_TransitionsToSynthesis_FromTargetedLoop()
    {
        var session = CreateSession(phase: SessionPhase.TargetedLoop);

        var updated = _sut.TransitionToSynthesis(session);

        updated.Phase.Should().Be(SessionPhase.Synthesis);
    }

    [Theory]
    [InlineData(SessionPhase.DraftRound3)]
    [InlineData(SessionPhase.Synthesis)]
    [InlineData(SessionPhase.Deliver)]
    [InlineData(SessionPhase.PostDelivery)]
    public void TransitionToSynthesis_ThrowsInvalidOperationException_FromInvalidPhase(SessionPhase invalidPhase)
    {
        var session = CreateSession(phase: invalidPhase);

        var act = () => _sut.TransitionToSynthesis(session);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{invalidPhase}*");
    }

    #endregion

    #region TransitionFromSynthesis Tests

    [Fact]
    public void TransitionFromSynthesis_GoesToDeliver_WhenConvergedWithoutBlockingQuestions()
    {
        var session = CreateSession(phase: SessionPhase.Synthesis, targetedLoopCount: 0, friction: 50);
        var map = TruthMapState.CreateNew(session.SessionId);
        SetHighConvergence(map);

        var updated = _sut.TransitionFromSynthesis(session, map);

        updated.Phase.Should().Be(SessionPhase.Deliver);
        updated.Status.Should().Be(SessionStatus.Complete);
    }

    [Fact]
    public void TransitionFromSynthesis_GoesToTargetedLoop_WhenNotConvergedAndLoopsRemain()
    {
        var session = CreateSession(phase: SessionPhase.Synthesis, targetedLoopCount: 1, friction: 50);
        var map = TruthMapState.CreateNew(session.SessionId);

        var updated = _sut.TransitionFromSynthesis(session, map);

        updated.Phase.Should().Be(SessionPhase.TargetedLoop);
        updated.TargetedLoopCount.Should().Be(2);
        updated.Status.Should().Be(SessionStatus.Active);
    }

    [Fact]
    public void TransitionFromSynthesis_GoesToDeliverWithGaps_WhenLoopsExhausted()
    {
        var session = CreateSession(phase: SessionPhase.Synthesis, targetedLoopCount: 2, friction: 50);
        session.RoundPolicy = new RoundPolicy { MaxTargetedLoops = 2 };
        var map = TruthMapState.CreateNew(session.SessionId);

        var updated = _sut.TransitionFromSynthesis(session, map);

        updated.Phase.Should().Be(SessionPhase.DeliverWithGaps);
        updated.Status.Should().Be(SessionStatus.CompleteWithGaps);
    }

    [Fact]
    public void TransitionFromSynthesis_GoesToTargetedLoop_WhenConvergedButHasBlockingQuestions()
    {
        var session = CreateSession(phase: SessionPhase.Synthesis, targetedLoopCount: 0, friction: 50);
        var map = TruthMapState.CreateNew(session.SessionId);
        SetHighConvergence(map);
        map.OpenQuestions.Add(new OpenQuestion
        {
            Id = Guid.NewGuid().ToString(),
            Text = "What is the deployment strategy?",
            Blocking = true,
            RaisedBy = "synthesizer"
        });

        var updated = _sut.TransitionFromSynthesis(session, map);

        updated.Phase.Should().Be(SessionPhase.TargetedLoop);
        updated.Status.Should().Be(SessionStatus.Active);
    }

    [Fact]
    public void TransitionFromSynthesis_GoesToDeliver_WhenConvergedWithNonBlockingQuestions()
    {
        var session = CreateSession(phase: SessionPhase.Synthesis, targetedLoopCount: 0, friction: 50);
        var map = TruthMapState.CreateNew(session.SessionId);
        SetHighConvergence(map);
        map.OpenQuestions.Add(new OpenQuestion
        {
            Id = Guid.NewGuid().ToString(),
            Text = "Nice to have feature?",
            Blocking = false,
            RaisedBy = "synthesizer"
        });

        var updated = _sut.TransitionFromSynthesis(session, map);

        updated.Phase.Should().Be(SessionPhase.Deliver);
        updated.Status.Should().Be(SessionStatus.Complete);
    }

    [Fact]
    public void TransitionFromSynthesis_ThrowsInvalidOperationException_FromNonSynthesisPhase()
    {
        var session = CreateSession(phase: SessionPhase.Critique);
        var map = TruthMapState.CreateNew(session.SessionId);

        var act = () => _sut.TransitionFromSynthesis(session, map);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Critique*");
    }

    [Fact]
    public void TransitionFromSynthesis_RespectsHighFrictionThresholds()
    {
        var session = CreateSession(phase: SessionPhase.Synthesis, targetedLoopCount: 0, friction: 85);
        var map = TruthMapState.CreateNew(session.SessionId);
        // Set convergence that would pass standard threshold but not high friction
        map.Convergence.ClaritySpecificity = 0.75f;
        map.Convergence.Feasibility = 0.75f;
        map.Convergence.RiskCoverage = 0.75f;
        map.Convergence.AssumptionExplicitness = 0.75f; // Below 0.8 required for high friction
        map.Convergence.Coherence = 0.85f;
        map.Convergence.Actionability = 0.75f;
        map.Convergence.EvidenceQuality = 0.65f; // Below 0.7 required for high friction

        var updated = _sut.TransitionFromSynthesis(session, map);

        updated.Phase.Should().Be(SessionPhase.TargetedLoop);
    }

    #endregion

    #region GetNextPhase Tests

    [Theory]
    [InlineData(SessionPhase.Intake, SessionPhase.Clarification)]
    [InlineData(SessionPhase.Clarification, SessionPhase.DraftRound1)]
    [InlineData(SessionPhase.DraftRound1, SessionPhase.DraftRound2)]
    [InlineData(SessionPhase.DraftRound2, SessionPhase.DraftRound3)]
    [InlineData(SessionPhase.DraftRound3, SessionPhase.Critique)]
    [InlineData(SessionPhase.Critique, SessionPhase.Synthesis)]
    [InlineData(SessionPhase.TargetedLoop, SessionPhase.Synthesis)]
    public void GetNextPhase_ReturnsCorrectNextPhase(SessionPhase current, SessionPhase expected)
    {
        var result = Orchestrator.GetNextPhase(current);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(SessionPhase.Synthesis)]
    [InlineData(SessionPhase.Deliver)]
    [InlineData(SessionPhase.DeliverWithGaps)]
    [InlineData(SessionPhase.PostDelivery)]
    public void GetNextPhase_ThrowsInvalidOperationException_ForTerminalPhases(SessionPhase terminalPhase)
    {
        var act = () => Orchestrator.GetNextPhase(terminalPhase);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{terminalPhase}*");
    }

    #endregion

    #region Helpers

    private static SessionState CreateSession(
        SessionPhase phase = SessionPhase.Clarification,
        int targetedLoopCount = 0,
        int friction = 50)
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

    private static void SetHighConvergence(TruthMapState map)
    {
        map.Convergence.ClaritySpecificity = 0.9f;
        map.Convergence.Feasibility = 0.9f;
        map.Convergence.RiskCoverage = 0.9f;
        map.Convergence.AssumptionExplicitness = 0.9f;
        map.Convergence.Coherence = 0.9f;
        map.Convergence.Actionability = 0.9f;
        map.Convergence.EvidenceQuality = 0.9f;
    }

    #endregion
}
