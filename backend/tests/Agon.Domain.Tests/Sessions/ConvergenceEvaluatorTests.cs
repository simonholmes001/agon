using Agon.Domain.Sessions;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.Sessions;

public class ConvergenceEvaluatorTests
{
    private static readonly RoundPolicy DefaultPolicy = new();

    // --- Overall score calculation ---

    [Fact]
    public void CalculateOverall_ReturnsWeightedAverage()
    {
        var convergence = new Convergence
        {
            ClaritySpecificity = 0.8f,
            Feasibility = 0.7f,
            RiskCoverage = 0.6f,
            AssumptionExplicitness = 0.75f,
            Coherence = 0.85f,
            Actionability = 0.7f,
            EvidenceQuality = 0.5f
        };

        var overall = ConvergenceEvaluator.CalculateOverall(convergence);

        // Equally weighted average: (0.8 + 0.7 + 0.6 + 0.75 + 0.85 + 0.7 + 0.5) / 7 ≈ 0.7
        overall.Should().BeApproximately(0.7f, 0.01f);
    }

    [Fact]
    public void CalculateOverall_ReturnsZero_WhenAllDimensionsAreZero()
    {
        var convergence = new Convergence();

        var overall = ConvergenceEvaluator.CalculateOverall(convergence);

        overall.Should().Be(0.0f);
    }

    [Fact]
    public void CalculateOverall_ReturnsOne_WhenAllDimensionsAreOne()
    {
        var convergence = new Convergence
        {
            ClaritySpecificity = 1.0f,
            Feasibility = 1.0f,
            RiskCoverage = 1.0f,
            AssumptionExplicitness = 1.0f,
            Coherence = 1.0f,
            Actionability = 1.0f,
            EvidenceQuality = 1.0f
        };

        var overall = ConvergenceEvaluator.CalculateOverall(convergence);

        overall.Should().Be(1.0f);
    }

    // --- Evaluate convergence status ---

    [Fact]
    public void Evaluate_ReturnsConverged_WhenOverallExceedsStandardThreshold()
    {
        var convergence = AllDimensionsAt(0.8f);

        var result = ConvergenceEvaluator.Evaluate(convergence, frictionLevel: 50, DefaultPolicy);

        result.Status.Should().Be(ConvergenceStatus.Converged);
        result.Overall.Should().BeApproximately(0.8f, 0.01f);
        result.Threshold.Should().Be(0.75f);
    }

    [Fact]
    public void Evaluate_ReturnsGapsRemain_WhenOverallBelowStandardThreshold()
    {
        var convergence = AllDimensionsAt(0.5f);

        var result = ConvergenceEvaluator.Evaluate(convergence, frictionLevel: 50, DefaultPolicy);

        result.Status.Should().Be(ConvergenceStatus.GapsRemain);
    }

    [Fact]
    public void Evaluate_UsesHighFrictionThreshold_WhenFrictionAboveCutoff()
    {
        // 0.8 passes standard (0.75) but fails high-friction (0.85)
        var convergence = AllDimensionsAt(0.8f);

        var result = ConvergenceEvaluator.Evaluate(convergence, frictionLevel: 80, DefaultPolicy);

        result.Status.Should().Be(ConvergenceStatus.GapsRemain);
        result.Threshold.Should().Be(0.85f);
    }

    [Fact]
    public void Evaluate_Converges_WithHighFriction_WhenScoreExceedsHighThreshold()
    {
        var convergence = AllDimensionsAt(0.9f);

        var result = ConvergenceEvaluator.Evaluate(convergence, frictionLevel: 80, DefaultPolicy);

        result.Status.Should().Be(ConvergenceStatus.Converged);
    }

    [Fact]
    public void Evaluate_SetsThresholdOnResult()
    {
        var convergence = AllDimensionsAt(0.5f);

        var result = ConvergenceEvaluator.Evaluate(convergence, frictionLevel: 50, DefaultPolicy);

        result.Threshold.Should().Be(0.75f);
    }

    // --- Weak dimensions ---

    [Fact]
    public void GetWeakDimensions_ReturnsNames_BelowThreshold()
    {
        var convergence = new Convergence
        {
            ClaritySpecificity = 0.9f,
            Feasibility = 0.9f,
            RiskCoverage = 0.3f,   // weak
            AssumptionExplicitness = 0.9f,
            Coherence = 0.9f,
            Actionability = 0.2f,  // weak
            EvidenceQuality = 0.9f
        };

        var weak = ConvergenceEvaluator.GetWeakDimensions(convergence, threshold: 0.75f);

        weak.Should().Contain("RiskCoverage");
        weak.Should().Contain("Actionability");
        weak.Should().HaveCount(2);
    }

    [Fact]
    public void GetWeakDimensions_ReturnsEmpty_WhenAllAboveThreshold()
    {
        var convergence = AllDimensionsAt(0.9f);

        var weak = ConvergenceEvaluator.GetWeakDimensions(convergence, threshold: 0.75f);

        weak.Should().BeEmpty();
    }

    private static Convergence AllDimensionsAt(float value)
    {
        return new Convergence
        {
            ClaritySpecificity = value,
            Feasibility = value,
            RiskCoverage = value,
            AssumptionExplicitness = value,
            Coherence = value,
            Actionability = value,
            EvidenceQuality = value
        };
    }
}
