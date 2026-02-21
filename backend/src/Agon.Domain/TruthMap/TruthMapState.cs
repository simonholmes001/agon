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
}
