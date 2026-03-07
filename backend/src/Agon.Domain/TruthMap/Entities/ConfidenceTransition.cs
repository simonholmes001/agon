namespace Agon.Domain.TruthMap.Entities;

public enum ConfidenceTransitionReason
{
    ChallengedNoDefense,
    EvidenceCorroboration,
    ManualOverride
}

public sealed record ConfidenceTransition(
    string ClaimId,
    float FromConfidence,
    float ToConfidence,
    ConfidenceTransitionReason Reason,
    int Round,
    DateTimeOffset OccurredAt);
