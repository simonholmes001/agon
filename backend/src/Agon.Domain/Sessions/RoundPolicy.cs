using Agon.Domain.Engines;

namespace Agon.Domain.Sessions;

/// <summary>
/// Session-level policy configuration. Immutable once created.
/// Governs round limits, budget, and convergence thresholds.
/// </summary>
public sealed record RoundPolicy
{
    public int MaxClarificationRounds { get; init; } = 2;
    public int MaxDebateRounds { get; init; } = 2;
    public int MaxTargetedLoops { get; init; } = 2;
    public int MaxSessionBudgetTokens { get; init; } = 200_000;

    /// <summary>Standard convergence threshold (friction 0–70).</summary>
    public float ConvergenceThresholdStandard { get; init; } = 0.75f;

    /// <summary>High-friction convergence threshold (friction > 70).</summary>
    public float ConvergenceThresholdHighFriction { get; init; } = 0.85f;

    /// <summary>Friction level at or above which the high-friction threshold applies.</summary>
    public int HighFrictionCutoff { get; init; } = 70;

    public ConfidenceDecayConfig ConfidenceDecay { get; init; } = new();

    /// <summary>Per-agent wall-clock timeout in seconds for any single agent call.</summary>
    public int AgentTimeoutSeconds { get; init; } = 90;

    // ── Factory ──────────────────────────────────────────────────────────────

    public static RoundPolicy Default() => new();

    // ── Computed helpers ─────────────────────────────────────────────────────

    /// <summary>Returns the convergence threshold appropriate for the given friction level.</summary>
    public float GetConvergenceThreshold(int frictionLevel) =>
        frictionLevel >= HighFrictionCutoff
            ? ConvergenceThresholdHighFriction
            : ConvergenceThresholdStandard;

    /// <summary>Returns the minimum assumption_explicitness score required to converge.</summary>
    public float GetMinAssumptionExplicitness(int frictionLevel) =>
        frictionLevel >= HighFrictionCutoff ? 0.80f : 0.70f;

    /// <summary>Returns the minimum evidence_quality score required to converge.</summary>
    public float GetMinEvidenceQuality(int frictionLevel) =>
        frictionLevel >= HighFrictionCutoff ? 0.70f : 0.50f;

    /// <summary>
    /// Determines whether the session should terminate early due to convergence.
    /// Returns true when the overall score meets the threshold AND
    /// the per-dimension minimums are met AND there are no blocking open questions.
    /// </summary>
    public bool ShouldConverge(
        float overallScore,
        float assumptionExplicitness,
        float evidenceQuality,
        bool hasBlockingOpenQuestions,
        int frictionLevel)
    {
        if (hasBlockingOpenQuestions) return false;
        var threshold = GetConvergenceThreshold(frictionLevel);
        return overallScore >= threshold
               && assumptionExplicitness >= GetMinAssumptionExplicitness(frictionLevel)
               && evidenceQuality >= GetMinEvidenceQuality(frictionLevel);
    }

    /// <summary>
    /// Returns true when the budget is considered exhausted.
    /// </summary>
    public bool IsBudgetExhausted(int tokensUsed) => tokensUsed >= MaxSessionBudgetTokens;

    /// <summary>Returns budget utilisation as a value between 0 and 1.</summary>
    public float BudgetUtilisation(int tokensUsed) =>
        MaxSessionBudgetTokens == 0 ? 1f : (float)tokensUsed / MaxSessionBudgetTokens;
}
