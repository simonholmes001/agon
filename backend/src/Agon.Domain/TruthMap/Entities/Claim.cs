namespace Agon.Domain.TruthMap.Entities;

public enum ClaimStatus { Active, Contested, PendingRevalidation }

public sealed record Claim(
    string Id,
    string ProposedBy,
    int Round,
    string Text,
    float Confidence,
    ClaimStatus Status,
    IReadOnlyList<string> DerivedFrom,
    IReadOnlyList<string> ChallengedBy);
