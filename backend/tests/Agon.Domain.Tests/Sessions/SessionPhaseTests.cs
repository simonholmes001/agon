using Agon.Domain.Sessions;
using FluentAssertions;

namespace Agon.Domain.Tests.Sessions;

public class SessionPhaseTests
{
    [Fact]
    public void SessionPhase_ContainsAllExpectedPhases()
    {
        var phases = Enum.GetValues<SessionPhase>();

        phases.Should().Contain(SessionPhase.Intake);
        phases.Should().Contain(SessionPhase.Clarification);
        phases.Should().Contain(SessionPhase.DraftRound1);
        phases.Should().Contain(SessionPhase.DraftRound2);
        phases.Should().Contain(SessionPhase.DraftRound3);
        phases.Should().Contain(SessionPhase.Critique);
        phases.Should().Contain(SessionPhase.Synthesis);
        phases.Should().Contain(SessionPhase.TargetedLoop);
        phases.Should().Contain(SessionPhase.Deliver);
        phases.Should().Contain(SessionPhase.DeliverWithGaps);
        phases.Should().Contain(SessionPhase.PostDelivery);
    }

    [Fact]
    public void SessionPhase_HasExactlyElevenPhases()
    {
        Enum.GetValues<SessionPhase>().Should().HaveCount(11);
    }

    [Fact]
    public void SessionMode_ContainsQuickAndDeep()
    {
        var modes = Enum.GetValues<SessionMode>();

        modes.Should().Contain(SessionMode.Quick);
        modes.Should().Contain(SessionMode.Deep);
        modes.Should().HaveCount(2);
    }

    [Fact]
    public void SessionStatus_ContainsAllExpectedStatuses()
    {
        var statuses = Enum.GetValues<SessionStatus>();

        statuses.Should().Contain(SessionStatus.Active);
        statuses.Should().Contain(SessionStatus.Paused);
        statuses.Should().Contain(SessionStatus.Complete);
        statuses.Should().Contain(SessionStatus.CompleteWithGaps);
        statuses.Should().Contain(SessionStatus.Forked);
        statuses.Should().Contain(SessionStatus.Closed);
        statuses.Should().HaveCount(6);
    }
}
