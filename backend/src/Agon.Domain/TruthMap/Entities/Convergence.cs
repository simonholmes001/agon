namespace Agon.Domain.TruthMap.Entities;

public enum ConvergenceStatus { InProgress, Converged, GapsRemain }

/// <summary>
/// Rubric-scored convergence state for a session.
/// All dimension scores are in [0.0, 1.0].
/// </summary>
public sealed record Convergence(
    float ClaritySpecificity,
    float Feasibility,
    float RiskCoverage,
    float AssumptionExplicitness,
    float Coherence,
    float Actionability,
    float EvidenceQuality,
    float Overall,
    float Threshold,
    ConvergenceStatus Status)
{
    public static Convergence Empty(float threshold) => new(
        ClaritySpecificity: 0f,
        Feasibility: 0f,
        RiskCoverage: 0f,
        AssumptionExplicitness: 0f,
        Coherence: 0f,
        Actionability: 0f,
        EvidenceQuality: 0f,
        Overall: 0f,
        Threshold: threshold,
        Status: ConvergenceStatus.InProgress);
}
