using Agon.Domain.Sessions;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.Sessions;

public class ConvergenceEvaluatorTests
{
    private static ConvergenceEvaluator BuildEvaluator(RoundPolicy? policy = null) =>
        new(policy ?? RoundPolicy.Default());

    private static ConvergenceInput AllHighInput(
        int frictionLevel = 40,
        bool hasBlocking = false,
        bool researchEnabled = true) =>
        new(
            ClaritySpecificity: 0.90f,
            Feasibility: 0.90f,
            RiskCoverage: 0.90f,
            AssumptionExplicitness: 0.90f,
            Coherence: 0.90f,
            Actionability: 0.90f,
            EvidenceQuality: 0.90f,
            HasBlockingOpenQuestions: hasBlocking,
            FrictionLevel: frictionLevel,
            ResearchToolsEnabled: researchEnabled);

    // ── Convergence status ────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_AllHighScores_NoBlocking_Converges()
    {
        var result = BuildEvaluator().Evaluate(AllHighInput());

        result.Status.Should().Be(ConvergenceStatus.Converged);
        result.Overall.Should().BeGreaterThanOrEqualTo(0.75f);
    }

    [Fact]
    public void Evaluate_BlockingOpenQuestion_IsNotConverged()
    {
        var result = BuildEvaluator().Evaluate(AllHighInput(hasBlocking: true));

        result.Status.Should().Be(ConvergenceStatus.GapsRemain);
    }

    [Fact]
    public void Evaluate_LowOverall_IsNotConverged()
    {
        var low = new ConvergenceInput(0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f,
            HasBlockingOpenQuestions: false, FrictionLevel: 40, ResearchToolsEnabled: true);

        var result = BuildEvaluator().Evaluate(low);

        result.Status.Should().Be(ConvergenceStatus.GapsRemain);
        result.Overall.Should().BeLessThan(0.75f);
    }

    // ── High-friction threshold ───────────────────────────────────────────────

    [Fact]
    public void Evaluate_HighFriction_HigherThresholdApplied()
    {
        // overall ~0.80 — passes standard (0.75) but not high-friction (0.85)
        var input = new ConvergenceInput(0.80f, 0.80f, 0.80f, 0.82f, 0.82f, 0.80f, 0.80f,
            HasBlockingOpenQuestions: false, FrictionLevel: 70, ResearchToolsEnabled: true);

        var result = BuildEvaluator().Evaluate(input);

        result.Threshold.Should().BeApproximately(0.85f, 0.001f);
        result.Status.Should().Be(ConvergenceStatus.GapsRemain);
    }

    [Fact]
    public void Evaluate_HighFriction_ConvergesWhenAboveHighThreshold()
    {
        var input = new ConvergenceInput(0.95f, 0.95f, 0.95f, 0.90f, 0.95f, 0.90f, 0.90f,
            HasBlockingOpenQuestions: false, FrictionLevel: 70, ResearchToolsEnabled: true);

        var result = BuildEvaluator().Evaluate(input);

        result.Status.Should().Be(ConvergenceStatus.Converged);
    }

    // ── Evidence quality cap when research tools disabled ────────────────────

    [Fact]
    public void Evaluate_ResearchToolsDisabled_EvidenceQualityCappedAt06()
    {
        var input = AllHighInput(researchEnabled: false) with { EvidenceQuality = 1.0f };

        var result = BuildEvaluator().Evaluate(input);

        result.EvidenceQuality.Should().BeApproximately(0.6f, 0.001f);
    }

    [Fact]
    public void Evaluate_ResearchToolsEnabled_EvidenceQualityNotCapped()
    {
        var input = AllHighInput(researchEnabled: true) with { EvidenceQuality = 1.0f };

        var result = BuildEvaluator().Evaluate(input);

        result.EvidenceQuality.Should().BeApproximately(1.0f, 0.001f);
    }

    // ── Weighted overall score ────────────────────────────────────────────────

    [Fact]
    public void Evaluate_Overall_IsWeightedAverage()
    {
        // All dimensions 1.0 → overall = sum of weights = 1.0
        var input = AllHighInput() with
        {
            ClaritySpecificity = 1f,
            Feasibility = 1f,
            RiskCoverage = 1f,
            AssumptionExplicitness = 1f,
            Coherence = 1f,
            Actionability = 1f,
            EvidenceQuality = 1f
        };

        var result = BuildEvaluator().Evaluate(input);

        result.Overall.Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void Evaluate_AllZero_OverallIsZero()
    {
        var input = new ConvergenceInput(0f, 0f, 0f, 0f, 0f, 0f, 0f,
            false, 40, true);

        var result = BuildEvaluator().Evaluate(input);

        result.Overall.Should().BeApproximately(0f, 0.001f);
    }

    // ── GetGapDimensions ──────────────────────────────────────────────────────

    [Fact]
    public void GetGapDimensions_ReturnsLowScoringDimensions()
    {
        var evaluator = BuildEvaluator();
        var convergence = evaluator.Evaluate(new ConvergenceInput(
            0.50f, // ClaritySpecificity — gap (< 0.70)
            0.85f, // Feasibility — ok
            0.50f, // RiskCoverage — gap (< 0.70)
            0.80f, // AssumptionExplicitness
            0.85f, // Coherence
            0.85f, // Actionability
            0.60f, // EvidenceQuality
            false, 40, true));

        var gaps = evaluator.GetGapDimensions(convergence, frictionLevel: 40);

        gaps.Should().Contain(nameof(convergence.ClaritySpecificity));
        gaps.Should().Contain(nameof(convergence.RiskCoverage));
        gaps.Should().NotContain(nameof(convergence.Feasibility));
    }

    [Fact]
    public void GetGapDimensions_Empty_WhenAllDimensionsPass()
    {
        var evaluator = BuildEvaluator();
        var convergence = evaluator.Evaluate(AllHighInput());

        evaluator.GetGapDimensions(convergence, frictionLevel: 40)
            .Should().BeEmpty();
    }
}
