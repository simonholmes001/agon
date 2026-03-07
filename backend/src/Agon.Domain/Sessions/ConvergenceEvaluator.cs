using Agon.Domain.TruthMap.Entities;

namespace Agon.Domain.Sessions;

/// <summary>Input dimensions for a single convergence evaluation pass.</summary>
public sealed record ConvergenceInput(
    float ClaritySpecificity,
    float Feasibility,
    float RiskCoverage,
    float AssumptionExplicitness,
    float Coherence,
    float Actionability,
    float EvidenceQuality,
    bool HasBlockingOpenQuestions,
    int FrictionLevel,
    bool ResearchToolsEnabled);

/// <summary>
/// Evaluates session convergence by scoring each rubric dimension and computing
/// the weighted overall score. Friction level adjusts per-dimension minimums.
/// </summary>
public sealed class ConvergenceEvaluator
{
    // Dimension weights (must sum to 1.0)
    private const float ClarityWeight = 0.15f;
    private const float FeasibilityWeight = 0.20f;
    private const float RiskCoverageWeight = 0.15f;
    private const float AssumptionWeight = 0.15f;
    private const float CoherenceWeight = 0.15f;
    private const float ActionabilityWeight = 0.10f;
    private const float EvidenceWeight = 0.10f;

    private readonly RoundPolicy _policy;

    public ConvergenceEvaluator(RoundPolicy policy)
    {
        _policy = policy;
    }

    /// <summary>
    /// Produces an updated <see cref="Convergence"/> record from the provided dimension scores.
    /// </summary>
    public Convergence Evaluate(ConvergenceInput input)
    {
        // When research tools are disabled, evidence_quality is capped at 0.6.
        var effectiveEvidence = input.ResearchToolsEnabled
            ? input.EvidenceQuality
            : Math.Min(input.EvidenceQuality, 0.6f);

        var overall = ComputeOverall(
            input.ClaritySpecificity,
            input.Feasibility,
            input.RiskCoverage,
            input.AssumptionExplicitness,
            input.Coherence,
            input.Actionability,
            effectiveEvidence);

        var threshold = _policy.GetConvergenceThreshold(input.FrictionLevel);

        var converged = _policy.ShouldConverge(
            overall,
            input.AssumptionExplicitness,
            effectiveEvidence,
            input.HasBlockingOpenQuestions,
            input.FrictionLevel);

        var status = converged ? ConvergenceStatus.Converged : ConvergenceStatus.GapsRemain;

        return new Convergence(
            ClaritySpecificity: input.ClaritySpecificity,
            Feasibility: input.Feasibility,
            RiskCoverage: input.RiskCoverage,
            AssumptionExplicitness: input.AssumptionExplicitness,
            Coherence: input.Coherence,
            Actionability: input.Actionability,
            EvidenceQuality: effectiveEvidence,
            Overall: overall,
            Threshold: threshold,
            Status: status);
    }

    /// <summary>
    /// Returns the names of dimensions that have not yet met their per-friction minimum.
    /// Used by the Synthesizer to identify which targeted loop is needed.
    /// </summary>
    public IReadOnlyList<string> GetGapDimensions(Convergence convergence, int frictionLevel)
    {
        var gaps = new List<string>();

        if (convergence.ClaritySpecificity < 0.70f) gaps.Add(nameof(convergence.ClaritySpecificity));
        if (convergence.Feasibility < 0.70f) gaps.Add(nameof(convergence.Feasibility));
        if (convergence.RiskCoverage < 0.70f) gaps.Add(nameof(convergence.RiskCoverage));
        if (convergence.AssumptionExplicitness < _policy.GetMinAssumptionExplicitness(frictionLevel))
            gaps.Add(nameof(convergence.AssumptionExplicitness));
        if (convergence.Coherence < 0.80f) gaps.Add(nameof(convergence.Coherence));
        if (convergence.Actionability < 0.70f) gaps.Add(nameof(convergence.Actionability));
        if (convergence.EvidenceQuality < _policy.GetMinEvidenceQuality(frictionLevel))
            gaps.Add(nameof(convergence.EvidenceQuality));

        return gaps;
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private static float ComputeOverall(
        float clarity,
        float feasibility,
        float risk,
        float assumption,
        float coherence,
        float actionability,
        float evidence)
    {
        return clarity * ClarityWeight
               + feasibility * FeasibilityWeight
               + risk * RiskCoverageWeight
               + assumption * AssumptionWeight
               + coherence * CoherenceWeight
               + actionability * ActionabilityWeight
               + evidence * EvidenceWeight;
    }
}
