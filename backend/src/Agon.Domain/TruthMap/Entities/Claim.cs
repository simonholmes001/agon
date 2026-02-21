namespace Agon.Domain.TruthMap.Entities;

public enum ClaimStatus
{
    Active,
    Contested,
    PendingRevalidation
}

public class Claim
{
    public required string Id { get; init; }
    public required string Agent { get; init; }
    public required int Round { get; init; }
    public required string Text { get; set; }
    public float Confidence { get; set; }
    public ClaimStatus Status { get; set; } = ClaimStatus.Active;
    public List<string> DerivedFrom { get; init; } = new();
    public List<string> ChallengedBy { get; set; } = new();
}
