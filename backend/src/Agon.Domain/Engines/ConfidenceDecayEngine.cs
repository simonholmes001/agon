using Agon.Domain.TruthMap.Entities;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Domain.Engines;

/// <summary>
/// Runs after each round to update claim confidence scores based on challenge
/// and evidence activity observed in that round.
///
/// Algorithm per claim:
/// 1. If challenged AND no agent defended it this round -> apply decay.
/// 2. If new evidence linked via evidence.supports -> apply boost.
/// 3. Clamp confidence to [0.0, 1.0].
/// 4. If confidence drops below contested_threshold -> mark Contested.
/// 5. Write a ConfidenceTransition to the patch log.
/// </summary>
public sealed class ConfidenceDecayEngine
{
    private readonly ConfidenceDecayConfig _config;

    public ConfidenceDecayEngine(ConfidenceDecayConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Applies decay/boost to all claims in the map based on round activity.
    /// Returns the updated TruthMap (immutable) plus the list of transitions.
    /// </summary>
    public (TruthMapModel UpdatedMap, IReadOnlyList<ConfidenceTransition> Transitions) Apply(
        TruthMapModel map,
        RoundActivity activity)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(activity);

        var transitions = new List<ConfidenceTransition>();
        var updatedClaims = new List<Claim>();

        foreach (var claim in map.Claims)
        {
            var (updatedClaim, transition) = ProcessClaim(claim, map, activity, map.Round);
            updatedClaims.Add(updatedClaim);
            if (transition is not null) transitions.Add(transition);
        }

        var updatedMap = map with
        {
            Claims = updatedClaims,
            ConfidenceTransitions = map.ConfidenceTransitions.Concat(transitions).ToList()
        };

        return (updatedMap, transitions);
    }

    private (Claim, ConfidenceTransition?) ProcessClaim(
        Claim claim,
        TruthMapModel map,
        RoundActivity activity,
        int round)
    {
        var originalConfidence = claim.Confidence;
        var newConfidence = originalConfidence;
        ConfidenceTransitionReason? reason = null;

        var wasChallenged = activity.ChallengedClaimIds.Contains(claim.Id);
        var wasDefended = activity.DefendedClaimIds.Contains(claim.Id);
        var hasNewEvidence = HasNewSupportingEvidence(claim.Id, map, activity.NewEvidenceIds);

        // Rule 1: Decay on undefended challenge.
        if (wasChallenged && !wasDefended)
        {
            newConfidence -= _config.DecayStep;
            reason = ConfidenceTransitionReason.ChallengedNoDefense;
        }

        // Rule 2: Boost on supporting evidence (overrides decay reason).
        if (hasNewEvidence)
        {
            newConfidence += _config.BoostStep;
            reason = ConfidenceTransitionReason.EvidenceCorroboration;
        }

        // Rule 3: Clamp to [0, 1].
        newConfidence = Math.Clamp(newConfidence, 0f, 1f);

        if (Math.Abs(newConfidence - originalConfidence) < 0.0001f)
            return (claim, null);

        // Rule 4: Mark contested if below threshold.
        ClaimStatus newStatus;
        if (newConfidence < _config.ContestedThreshold)
        {
            newStatus = ClaimStatus.Contested;
        }
        else if (claim.Status == ClaimStatus.Contested)
        {
            newStatus = ClaimStatus.Active;
        }
        else
        {
            newStatus = claim.Status;
        }

        var updatedClaim = claim with { Confidence = newConfidence, Status = newStatus };

        var transition = new ConfidenceTransition(
            ClaimId: claim.Id,
            FromConfidence: originalConfidence,
            ToConfidence: newConfidence,
            Reason: reason!.Value,
            Round: round,
            OccurredAt: DateTimeOffset.UtcNow);

        return (updatedClaim, transition);
    }

    private static bool HasNewSupportingEvidence(
        string claimId,
        TruthMapModel map,
        IReadOnlySet<string> newEvidenceIds)
    {
        return map.Evidence
            .Where(e => newEvidenceIds.Contains(e.Id))
            .Any(e => e.Supports.Contains(claimId));
    }
}

/// <summary>
/// Describes the challenge/defense/evidence activity during a single round.
/// Built by the Orchestrator after processing all agent patches.
/// </summary>
public sealed record RoundActivity(
    IReadOnlySet<string> ChallengedClaimIds,
    IReadOnlySet<string> DefendedClaimIds,
    IReadOnlySet<string> NewEvidenceIds)
{
    public static RoundActivity Empty() => new(
        new HashSet<string>(),
        new HashSet<string>(),
        new HashSet<string>());
}
