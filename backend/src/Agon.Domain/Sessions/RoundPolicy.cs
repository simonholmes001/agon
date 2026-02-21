namespace Agon.Domain.Sessions;

/// <summary>
/// The Orchestrator's session configuration — loop limits, budget, and convergence thresholds.
/// Defaults from the schemas specification. Can be overridden per session tier.
/// </summary>
public class RoundPolicy
{
    public int MaxClarificationRounds { get; init; } = 2;
    public int MaxDebateRounds { get; init; } = 2;
    public int MaxTargetedLoops { get; init; } = 2;
    public int MaxSessionBudgetTokens { get; init; } = 200_000;
    public float ConvergenceThresholdStandard { get; init; } = 0.75f;
    public float ConvergenceThresholdHighFriction { get; init; } = 0.85f;
    public int HighFrictionCutoff { get; init; } = 70;

    public bool ShouldTerminateClarification(int currentRound) =>
        currentRound >= MaxClarificationRounds;

    public bool ShouldTerminateDebate(int currentRound) =>
        currentRound >= MaxDebateRounds;

    public bool ShouldTerminateTargetedLoop(int currentLoop) =>
        currentLoop >= MaxTargetedLoops;

    public bool IsBudgetExhausted(int tokensUsed) =>
        tokensUsed >= MaxSessionBudgetTokens;

    /// <summary>
    /// Returns the convergence threshold appropriate for the given friction level.
    /// High friction (>= cutoff) demands a higher bar for convergence.
    /// </summary>
    public float GetConvergenceThreshold(int frictionLevel) =>
        frictionLevel >= HighFrictionCutoff
            ? ConvergenceThresholdHighFriction
            : ConvergenceThresholdStandard;
}
