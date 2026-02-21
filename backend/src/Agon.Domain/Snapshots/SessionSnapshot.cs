using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agon.Domain.TruthMap;

namespace Agon.Domain.Snapshots;

/// <summary>
/// Immutable snapshot written at the end of every round.
/// Used for Pause-and-Replay and session history.
/// </summary>
public class SessionSnapshot
{
    public Guid SnapshotId { get; init; }
    public Guid SessionId { get; init; }
    public int Round { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string TruthMapHash { get; init; } = string.Empty;
    public TruthMapState TruthMap { get; init; } = null!;

    private SessionSnapshot() { }

    /// <summary>
    /// Creates a new immutable snapshot from the current Truth Map state.
    /// Computes a SHA-256 hash of the Truth Map for integrity verification.
    /// </summary>
    public static SessionSnapshot Create(Guid sessionId, int round, TruthMapState truthMap)
    {
        var frozenTruthMap = truthMap.DeepCopy();

        return new SessionSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            SessionId = sessionId,
            Round = round,
            CreatedAt = DateTimeOffset.UtcNow,
            TruthMapHash = ComputeHash(frozenTruthMap),
            TruthMap = frozenTruthMap
        };
    }

    private static string ComputeHash(TruthMapState map)
    {
        // Canonical JSON covers the full TruthMap state with stable ordering.
        var canonical = new
        {
            session_id = map.SessionId,
            version = map.Version,
            round = map.Round,
            core_idea = map.CoreIdea,
            constraints = new
            {
                budget = map.Constraints.Budget,
                timeline = map.Constraints.Timeline,
                tech_stack = map.Constraints.TechStack.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                non_negotiables = map.Constraints.NonNegotiables.OrderBy(value => value, StringComparer.Ordinal).ToArray()
            },
            success_metrics = map.SuccessMetrics.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            personas = map.Personas
                .OrderBy(persona => persona.Id, StringComparer.Ordinal)
                .Select(persona => new
                {
                    id = persona.Id,
                    name = persona.Name,
                    description = persona.Description
                })
                .ToArray(),
            claims = map.Claims
                .OrderBy(claim => claim.Id, StringComparer.Ordinal)
                .Select(claim => new
                {
                    id = claim.Id,
                    agent = claim.Agent,
                    round = claim.Round,
                    text = claim.Text,
                    confidence = claim.Confidence,
                    status = claim.Status.ToString(),
                    derived_from = claim.DerivedFrom.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    challenged_by = claim.ChallengedBy.OrderBy(value => value, StringComparer.Ordinal).ToArray()
                })
                .ToArray(),
            assumptions = map.Assumptions
                .OrderBy(assumption => assumption.Id, StringComparer.Ordinal)
                .Select(assumption => new
                {
                    id = assumption.Id,
                    text = assumption.Text,
                    validation_step = assumption.ValidationStep,
                    status = assumption.Status.ToString(),
                    derived_from = assumption.DerivedFrom.OrderBy(value => value, StringComparer.Ordinal).ToArray()
                })
                .ToArray(),
            decisions = map.Decisions
                .OrderBy(decision => decision.Id, StringComparer.Ordinal)
                .Select(decision => new
                {
                    id = decision.Id,
                    text = decision.Text,
                    rationale = decision.Rationale,
                    owner = decision.Owner,
                    binding = decision.Binding,
                    derived_from = decision.DerivedFrom.OrderBy(value => value, StringComparer.Ordinal).ToArray()
                })
                .ToArray(),
            risks = map.Risks
                .OrderBy(risk => risk.Id, StringComparer.Ordinal)
                .Select(risk => new
                {
                    id = risk.Id,
                    text = risk.Text,
                    category = risk.Category.ToString(),
                    severity = risk.Severity.ToString(),
                    likelihood = risk.Likelihood.ToString(),
                    mitigation = risk.Mitigation,
                    agent = risk.Agent,
                    derived_from = risk.DerivedFrom.OrderBy(value => value, StringComparer.Ordinal).ToArray()
                })
                .ToArray(),
            open_questions = map.OpenQuestions
                .OrderBy(question => question.Id, StringComparer.Ordinal)
                .Select(question => new
                {
                    id = question.Id,
                    text = question.Text,
                    blocking = question.Blocking,
                    raised_by = question.RaisedBy
                })
                .ToArray(),
            evidence = map.Evidence
                .OrderBy(evidence => evidence.Id, StringComparer.Ordinal)
                .Select(evidence => new
                {
                    id = evidence.Id,
                    title = evidence.Title,
                    source = evidence.Source,
                    retrieved_at = evidence.RetrievedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    summary = evidence.Summary,
                    supports = evidence.Supports.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    contradicts = evidence.Contradicts.OrderBy(value => value, StringComparer.Ordinal).ToArray()
                })
                .ToArray(),
            convergence = new
            {
                clarity_specificity = map.Convergence.ClaritySpecificity,
                feasibility = map.Convergence.Feasibility,
                risk_coverage = map.Convergence.RiskCoverage,
                assumption_explicitness = map.Convergence.AssumptionExplicitness,
                coherence = map.Convergence.Coherence,
                actionability = map.Convergence.Actionability,
                evidence_quality = map.Convergence.EvidenceQuality,
                overall = map.Convergence.Overall,
                threshold = map.Convergence.Threshold,
                status = map.Convergence.Status.ToString()
            },
            confidence_transitions = map.ConfidenceTransitions
                .OrderBy(transition => transition.ClaimId, StringComparer.Ordinal)
                .ThenBy(transition => transition.Round)
                .ThenBy(transition => transition.Reason)
                .Select(transition => new
                {
                    claim_id = transition.ClaimId,
                    round = transition.Round,
                    from = transition.From,
                    to = transition.To,
                    reason = transition.Reason.ToString()
                })
                .ToArray()
        };

        var json = JsonSerializer.Serialize(canonical);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
