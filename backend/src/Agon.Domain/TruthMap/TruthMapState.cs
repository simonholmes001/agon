using Agon.Domain.TruthMap.Entities;

namespace Agon.Domain.TruthMap;

/// <summary>
/// The authoritative session state — the single source of truth.
/// Agents propose patches; the Orchestrator validates and applies them.
/// </summary>
public class TruthMapState
{
    public Guid SessionId { get; init; }
    public int Version { get; private set; }
    public int Round { get; set; }
    public string CoreIdea { get; set; } = string.Empty;

    public Constraints Constraints { get; set; } = new();
    public List<string> SuccessMetrics { get; set; } = new();
    public List<Persona> Personas { get; set; } = new();
    public List<Claim> Claims { get; set; } = new();
    public List<Assumption> Assumptions { get; set; } = new();
    public List<Decision> Decisions { get; set; } = new();
    public List<Risk> Risks { get; set; } = new();
    public List<OpenQuestion> OpenQuestions { get; set; } = new();
    public List<Evidence> Evidence { get; set; } = new();
    public Convergence Convergence { get; set; } = new();
    public List<ConfidenceTransition> ConfidenceTransitions { get; set; } = new();

    private TruthMapState() { }

    public static TruthMapState CreateNew(Guid sessionId)
    {
        return new TruthMapState
        {
            SessionId = sessionId,
            Version = 0,
            Round = 0
        };
    }

    public void IncrementVersion() => Version++;

    /// <summary>
    /// Checks whether any entity with the given ID exists anywhere in the Truth Map.
    /// </summary>
    public bool EntityExists(string entityId)
    {
        return Claims.Any(c => c.Id == entityId)
            || Assumptions.Any(a => a.Id == entityId)
            || Decisions.Any(d => d.Id == entityId)
            || Risks.Any(r => r.Id == entityId)
            || Evidence.Any(e => e.Id == entityId)
            || OpenQuestions.Any(q => q.Id == entityId)
            || Personas.Any(p => p.Id == entityId);
    }

    /// <summary>
    /// Finds a claim by ID, or returns null if not found.
    /// </summary>
    public Claim? FindClaim(string claimId)
    {
        return Claims.FirstOrDefault(c => c.Id == claimId);
    }

    /// <summary>
    /// Creates a deep copy of the full Truth Map state.
    /// </summary>
    public TruthMapState DeepCopy()
    {
        return new TruthMapState
        {
            SessionId = SessionId,
            Version = Version,
            Round = Round,
            CoreIdea = CoreIdea,
            Constraints = new Constraints
            {
                Budget = Constraints.Budget,
                Timeline = Constraints.Timeline,
                TechStack = [.. Constraints.TechStack],
                NonNegotiables = [.. Constraints.NonNegotiables]
            },
            SuccessMetrics = [.. SuccessMetrics],
            Personas = Personas
                .Select(persona => new Persona
                {
                    Id = persona.Id,
                    Name = persona.Name,
                    Description = persona.Description
                })
                .ToList(),
            Claims = Claims
                .Select(claim => new Claim
                {
                    Id = claim.Id,
                    Agent = claim.Agent,
                    Round = claim.Round,
                    Text = claim.Text,
                    Confidence = claim.Confidence,
                    Status = claim.Status,
                    DerivedFrom = [.. claim.DerivedFrom],
                    ChallengedBy = [.. claim.ChallengedBy]
                })
                .ToList(),
            Assumptions = Assumptions
                .Select(assumption => new Assumption
                {
                    Id = assumption.Id,
                    Text = assumption.Text,
                    ValidationStep = assumption.ValidationStep,
                    Status = assumption.Status,
                    DerivedFrom = [.. assumption.DerivedFrom]
                })
                .ToList(),
            Decisions = Decisions
                .Select(decision => new Decision
                {
                    Id = decision.Id,
                    Text = decision.Text,
                    Rationale = decision.Rationale,
                    Owner = decision.Owner,
                    Binding = decision.Binding,
                    DerivedFrom = [.. decision.DerivedFrom]
                })
                .ToList(),
            Risks = Risks
                .Select(risk => new Risk
                {
                    Id = risk.Id,
                    Text = risk.Text,
                    Category = risk.Category,
                    Severity = risk.Severity,
                    Likelihood = risk.Likelihood,
                    Mitigation = risk.Mitigation,
                    Agent = risk.Agent,
                    DerivedFrom = [.. risk.DerivedFrom]
                })
                .ToList(),
            OpenQuestions = OpenQuestions
                .Select(question => new OpenQuestion
                {
                    Id = question.Id,
                    Text = question.Text,
                    Blocking = question.Blocking,
                    RaisedBy = question.RaisedBy
                })
                .ToList(),
            Evidence = Evidence
                .Select(evidence => new Evidence
                {
                    Id = evidence.Id,
                    Title = evidence.Title,
                    Source = evidence.Source,
                    RetrievedAt = evidence.RetrievedAt,
                    Summary = evidence.Summary,
                    Supports = [.. evidence.Supports],
                    Contradicts = [.. evidence.Contradicts]
                })
                .ToList(),
            Convergence = new Convergence
            {
                ClaritySpecificity = Convergence.ClaritySpecificity,
                Feasibility = Convergence.Feasibility,
                RiskCoverage = Convergence.RiskCoverage,
                AssumptionExplicitness = Convergence.AssumptionExplicitness,
                Coherence = Convergence.Coherence,
                Actionability = Convergence.Actionability,
                EvidenceQuality = Convergence.EvidenceQuality,
                Overall = Convergence.Overall,
                Threshold = Convergence.Threshold,
                Status = Convergence.Status
            },
            ConfidenceTransitions = ConfidenceTransitions
                .Select(transition => new ConfidenceTransition
                {
                    ClaimId = transition.ClaimId,
                    Round = transition.Round,
                    From = transition.From,
                    To = transition.To,
                    Reason = transition.Reason
                })
                .ToList()
        };
    }
}
