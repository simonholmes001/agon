using Agon.Domain.TruthMap.Entities;

namespace Agon.Domain.TruthMap;

/// <summary>
/// The authoritative session state. All artifacts and scores are generated from this —
/// never from the raw agent transcript.
/// Version is incremented on every successfully applied patch.
/// </summary>
public sealed record TruthMap
{
    public Guid SessionId { get; init; }
    public int Version { get; init; }
    public int Round { get; init; }

    public string CoreIdea { get; init; } = string.Empty;
    public Constraints Constraints { get; init; } = new(string.Empty, string.Empty, [], []);
    public IReadOnlyList<string> SuccessMetrics { get; init; } = [];

    public IReadOnlyList<Persona> Personas { get; init; } = [];
    public IReadOnlyList<Claim> Claims { get; init; } = [];
    public IReadOnlyList<Assumption> Assumptions { get; init; } = [];
    public IReadOnlyList<Decision> Decisions { get; init; } = [];
    public IReadOnlyList<Risk> Risks { get; init; } = [];
    public IReadOnlyList<OpenQuestion> OpenQuestions { get; init; } = [];
    public IReadOnlyList<Evidence> Evidence { get; init; } = [];
    public IReadOnlyList<ConfidenceTransition> ConfidenceTransitions { get; init; } = [];

    public Convergence Convergence { get; init; } = Convergence.Empty(0.75f);

    // ── Factory ──────────────────────────────────────────────────────────────

    public static TruthMap Empty(Guid sessionId, float convergenceThreshold = 0.75f) => new()
    {
        SessionId = sessionId,
        Version = 0,
        Round = 0,
        Convergence = Convergence.Empty(convergenceThreshold)
    };

    // ── Lookup helpers ────────────────────────────────────────────────────────

    public Claim? FindClaim(string id) => Claims.FirstOrDefault(c => c.Id == id);
    public Assumption? FindAssumption(string id) => Assumptions.FirstOrDefault(a => a.Id == id);
    public Decision? FindDecision(string id) => Decisions.FirstOrDefault(d => d.Id == id);
    public Risk? FindRisk(string id) => Risks.FirstOrDefault(r => r.Id == id);
    public OpenQuestion? FindOpenQuestion(string id) => OpenQuestions.FirstOrDefault(q => q.Id == id);
    public Evidence? FindEvidence(string id) => Evidence.FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// Returns true if an entity with the given ID exists anywhere in the Truth Map.
    /// Used by PatchValidator to verify references.
    /// </summary>
    public bool EntityExists(string id) =>
        Claims.Any(c => c.Id == id)
        || Assumptions.Any(a => a.Id == id)
        || Decisions.Any(d => d.Id == id)
        || Risks.Any(r => r.Id == id)
        || OpenQuestions.Any(q => q.Id == id)
        || Evidence.Any(e => e.Id == id)
        || Personas.Any(p => p.Id == id);

    public bool HasBlockingOpenQuestions() =>
        OpenQuestions.Any(q => q.Blocking);

    public IReadOnlyList<Claim> GetContestedClaims() =>
        Claims.Where(c => c.Status == ClaimStatus.Contested).ToList();
}
