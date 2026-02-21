using Agon.Domain.TruthMap.Entities;

namespace Agon.Domain.Engines;

/// <summary>
/// Applies confidence decay on undefended challenge, boost on evidence,
/// clamps to [0.0, 1.0], and flags contested claims.
/// </summary>
public static class ConfidenceDecayEngine
{
    /// <summary>
    /// Decays a claim's confidence when it was challenged and not defended.
    /// Clamps result to [0.0, 1.0].
    /// </summary>
    public static ConfidenceTransition ApplyDecay(Claim claim, ConfidenceDecayConfig config)
    {
        var newConfidence = Math.Max(0.0f, claim.Confidence - config.DecayStep);

        return new ConfidenceTransition
        {
            ClaimId = claim.Id,
            Round = claim.Round,
            From = claim.Confidence,
            To = newConfidence,
            Reason = ConfidenceTransitionReason.ChallengedNoDefense
        };
    }

    /// <summary>
    /// Boosts a claim's confidence when supporting evidence is linked.
    /// Clamps result to [0.0, 1.0].
    /// </summary>
    public static ConfidenceTransition ApplyBoost(Claim claim, ConfidenceDecayConfig config)
    {
        var newConfidence = Math.Min(1.0f, claim.Confidence + config.BoostStep);

        return new ConfidenceTransition
        {
            ClaimId = claim.Id,
            Round = claim.Round,
            From = claim.Confidence,
            To = newConfidence,
            Reason = ConfidenceTransitionReason.EvidenceCorroboration
        };
    }

    /// <summary>
    /// Returns true if the confidence is at or below the contested threshold.
    /// </summary>
    public static bool IsContested(float confidence, ConfidenceDecayConfig config)
    {
        return confidence <= config.ContestedThreshold;
    }
}
