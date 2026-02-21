using Agon.Domain.TruthMap.Entities;

namespace Agon.Domain.Sessions;

/// <summary>
/// Evaluates convergence by scoring dimensions and comparing against
/// friction-adjusted thresholds.
/// </summary>
public static class ConvergenceEvaluator
{
    private static readonly (string Name, Func<Convergence, float> Getter)[] Dimensions =
    {
        ("ClaritySpecificity", c => c.ClaritySpecificity),
        ("Feasibility", c => c.Feasibility),
        ("RiskCoverage", c => c.RiskCoverage),
        ("AssumptionExplicitness", c => c.AssumptionExplicitness),
        ("Coherence", c => c.Coherence),
        ("Actionability", c => c.Actionability),
        ("EvidenceQuality", c => c.EvidenceQuality),
    };

    /// <summary>
    /// Calculates the overall convergence score as an equally weighted average
    /// of all dimensions.
    /// </summary>
    public static float CalculateOverall(Convergence convergence)
    {
        var sum = Dimensions.Sum(d => d.Getter(convergence));
        return sum / Dimensions.Length;
    }

    /// <summary>
    /// Evaluates convergence against the friction-adjusted threshold.
    /// Updates the Convergence object and returns it.
    /// </summary>
    public static Convergence Evaluate(Convergence convergence, int frictionLevel, RoundPolicy policy)
    {
        var threshold = policy.GetConvergenceThreshold(frictionLevel);
        var overall = CalculateOverall(convergence);

        convergence.Overall = overall;
        convergence.Threshold = threshold;
        convergence.Status = overall >= threshold
            ? ConvergenceStatus.Converged
            : ConvergenceStatus.GapsRemain;

        return convergence;
    }

    /// <summary>
    /// Returns the names of dimensions scoring below the given threshold.
    /// Used to identify which areas need targeted work.
    /// </summary>
    public static IReadOnlyList<string> GetWeakDimensions(Convergence convergence, float threshold)
    {
        return Dimensions
            .Where(d => d.Getter(convergence) < threshold)
            .Select(d => d.Name)
            .ToList();
    }
}
