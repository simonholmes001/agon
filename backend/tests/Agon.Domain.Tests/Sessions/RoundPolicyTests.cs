using Agon.Domain.Engines;
using Agon.Domain.Sessions;
using FluentAssertions;

namespace Agon.Domain.Tests.Sessions;

public class RoundPolicyTests
{
    // ── GetConvergenceThreshold ───────────────────────────────────────────────

    [Theory]
    [InlineData(0,  0.75f)]
    [InlineData(50, 0.75f)]
    [InlineData(69, 0.75f)]
    [InlineData(70, 0.85f)]
    [InlineData(100, 0.85f)]
    public void GetConvergenceThreshold_ReturnsCorrectThresholdForFrictionLevel(
        int frictionLevel, float expectedThreshold)
    {
        var policy = RoundPolicy.Default();
        policy.GetConvergenceThreshold(frictionLevel).Should().BeApproximately(expectedThreshold, 0.001f);
    }

    // ── IsBudgetExhausted ─────────────────────────────────────────────────────

    [Fact]
    public void IsBudgetExhausted_ReturnsFalse_WhenTokensUnderLimit()
    {
        var policy = RoundPolicy.Default();
        policy.IsBudgetExhausted(100_000).Should().BeFalse();
    }

    [Fact]
    public void IsBudgetExhausted_ReturnsTrue_AtExactLimit()
    {
        var policy = RoundPolicy.Default();
        policy.IsBudgetExhausted(200_000).Should().BeTrue();
    }

    [Fact]
    public void IsBudgetExhausted_ReturnsTrue_AboveLimit()
    {
        var policy = RoundPolicy.Default();
        policy.IsBudgetExhausted(250_000).Should().BeTrue();
    }

    // ── BudgetUtilisation ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0,       0.00f)]
    [InlineData(100_000, 0.50f)]
    [InlineData(200_000, 1.00f)]
    public void BudgetUtilisation_ReturnsCorrectFraction(int tokensUsed, float expected)
    {
        RoundPolicy.Default().BudgetUtilisation(tokensUsed)
            .Should().BeApproximately(expected, 0.001f);
    }

    [Fact]
    public void BudgetUtilisation_ZeroBudget_ReturnsOne()
    {
        var policy = new RoundPolicy { MaxSessionBudgetTokens = 0 };
        policy.BudgetUtilisation(1).Should().Be(1f);
    }

    // ── ShouldConverge ────────────────────────────────────────────────────────

    [Fact]
    public void ShouldConverge_ReturnsFalse_WhenBlockingOpenQuestionsExist()
    {
        var policy = RoundPolicy.Default();
        policy.ShouldConverge(0.80f, 0.80f, 0.80f, hasBlockingOpenQuestions: true, frictionLevel: 50)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldConverge_ReturnsFalse_WhenOverallScoreBelowStandardThreshold()
    {
        var policy = RoundPolicy.Default();
        policy.ShouldConverge(0.70f, 0.80f, 0.80f, hasBlockingOpenQuestions: false, frictionLevel: 50)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldConverge_ReturnsTrue_WhenAllConditionsMet_StandardFriction()
    {
        var policy = RoundPolicy.Default();
        policy.ShouldConverge(0.80f, 0.75f, 0.55f, hasBlockingOpenQuestions: false, frictionLevel: 50)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldConverge_ReturnsFalse_WhenHighFrictionThresholdNotMet()
    {
        var policy = RoundPolicy.Default();
        // overall = 0.80 which is >= 0.75 standard threshold but < 0.85 high-friction threshold
        policy.ShouldConverge(0.80f, 0.85f, 0.75f, hasBlockingOpenQuestions: false, frictionLevel: 70)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldConverge_ReturnsTrue_WhenHighFrictionThresholdMet()
    {
        var policy = RoundPolicy.Default();
        policy.ShouldConverge(0.87f, 0.85f, 0.75f, hasBlockingOpenQuestions: false, frictionLevel: 70)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldConverge_ReturnsFalse_WhenAssumptionExplicitnessToolow_HighFriction()
    {
        var policy = RoundPolicy.Default();
        // assumption explicitness 0.75 < 0.80 required at high friction
        policy.ShouldConverge(0.87f, 0.75f, 0.75f, hasBlockingOpenQuestions: false, frictionLevel: 70)
            .Should().BeFalse();
    }

    // ── Default values ────────────────────────────────────────────────────────

    [Fact]
    public void Default_HasExpectedValues()
    {
        var policy = RoundPolicy.Default();

        policy.MaxClarificationRounds.Should().Be(2);
        policy.MaxDebateRounds.Should().Be(2);
        policy.MaxTargetedLoops.Should().Be(2);
        policy.MaxSessionBudgetTokens.Should().Be(200_000);
        policy.ConvergenceThresholdStandard.Should().BeApproximately(0.75f, 0.001f);
        policy.ConvergenceThresholdHighFriction.Should().BeApproximately(0.85f, 0.001f);
        policy.HighFrictionCutoff.Should().Be(70);
    }

    [Fact]
    public void Default_ConfidenceDecayConfig_HasExpectedDefaults()
    {
        var policy = RoundPolicy.Default();

        policy.ConfidenceDecay.DecayStep.Should().BeApproximately(0.15f, 0.001f);
        policy.ConfidenceDecay.BoostStep.Should().BeApproximately(0.10f, 0.001f);
        policy.ConfidenceDecay.ContestedThreshold.Should().BeApproximately(0.30f, 0.001f);
    }
}
