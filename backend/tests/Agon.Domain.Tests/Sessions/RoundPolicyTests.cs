using Agon.Domain.Sessions;
using FluentAssertions;

namespace Agon.Domain.Tests.Sessions;

public class RoundPolicyTests
{
    // --- Default values ---

    [Fact]
    public void DefaultPolicy_HasCorrectLimits()
    {
        var policy = new RoundPolicy();

        policy.MaxClarificationRounds.Should().Be(2);
        policy.MaxDebateRounds.Should().Be(2);
        policy.MaxTargetedLoops.Should().Be(2);
        policy.MaxSessionBudgetTokens.Should().Be(200_000);
        policy.ConvergenceThresholdStandard.Should().Be(0.75f);
        policy.ConvergenceThresholdHighFriction.Should().Be(0.85f);
        policy.HighFrictionCutoff.Should().Be(70);
    }

    // --- Loop termination ---

    [Fact]
    public void ShouldTerminateClarification_True_WhenAtMaxRounds()
    {
        var policy = new RoundPolicy();

        policy.ShouldTerminateClarification(currentRound: 2).Should().BeTrue();
    }

    [Fact]
    public void ShouldTerminateClarification_False_WhenBelowMaxRounds()
    {
        var policy = new RoundPolicy();

        policy.ShouldTerminateClarification(currentRound: 1).Should().BeFalse();
    }

    [Fact]
    public void ShouldTerminateDebate_True_WhenAtMaxRounds()
    {
        var policy = new RoundPolicy();

        policy.ShouldTerminateDebate(currentRound: 2).Should().BeTrue();
    }

    [Fact]
    public void ShouldTerminateTargetedLoop_True_WhenAtMaxRounds()
    {
        var policy = new RoundPolicy();

        policy.ShouldTerminateTargetedLoop(currentLoop: 2).Should().BeTrue();
    }

    // --- Budget exhaustion ---

    [Fact]
    public void IsBudgetExhausted_True_WhenTokensExceedBudget()
    {
        var policy = new RoundPolicy();

        policy.IsBudgetExhausted(tokensUsed: 200_001).Should().BeTrue();
    }

    [Fact]
    public void IsBudgetExhausted_True_WhenTokensEqualBudget()
    {
        var policy = new RoundPolicy();

        policy.IsBudgetExhausted(tokensUsed: 200_000).Should().BeTrue();
    }

    [Fact]
    public void IsBudgetExhausted_False_WhenTokensBelowBudget()
    {
        var policy = new RoundPolicy();

        policy.IsBudgetExhausted(tokensUsed: 150_000).Should().BeFalse();
    }

    // --- Convergence threshold selection ---

    [Fact]
    public void GetConvergenceThreshold_ReturnsStandard_WhenFrictionBelowCutoff()
    {
        var policy = new RoundPolicy();

        policy.GetConvergenceThreshold(frictionLevel: 50).Should().Be(0.75f);
    }

    [Fact]
    public void GetConvergenceThreshold_ReturnsHighFriction_WhenFrictionAtCutoff()
    {
        var policy = new RoundPolicy();

        policy.GetConvergenceThreshold(frictionLevel: 70).Should().Be(0.85f);
    }

    [Fact]
    public void GetConvergenceThreshold_ReturnsHighFriction_WhenFrictionAboveCutoff()
    {
        var policy = new RoundPolicy();

        policy.GetConvergenceThreshold(frictionLevel: 90).Should().Be(0.85f);
    }

    // --- Custom policy ---

    [Fact]
    public void CustomPolicy_OverridesDefaults()
    {
        var policy = new RoundPolicy
        {
            MaxClarificationRounds = 3,
            MaxDebateRounds = 4,
            MaxSessionBudgetTokens = 500_000
        };

        policy.ShouldTerminateClarification(currentRound: 2).Should().BeFalse();
        policy.ShouldTerminateClarification(currentRound: 3).Should().BeTrue();
        policy.IsBudgetExhausted(tokensUsed: 300_000).Should().BeFalse();
    }
}
