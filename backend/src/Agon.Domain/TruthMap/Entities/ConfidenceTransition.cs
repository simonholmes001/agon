namespace Agon.Domain.TruthMap.Entities;

public enum ConfidenceTransitionReason
{
    ChallengedNoDefense,
    EvidenceCorroboration,
    ManualOverride
}

public class ConfidenceTransition
{
    public required string ClaimId { get; init; }
    public int Round { get; init; }
    public float From { get; init; }
    public float To { get; init; }
    public ConfidenceTransitionReason Reason { get; init; }
}
