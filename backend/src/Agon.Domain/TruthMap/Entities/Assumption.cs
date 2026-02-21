namespace Agon.Domain.TruthMap.Entities;

public enum AssumptionStatus
{
    Unvalidated,
    Validated,
    Invalidated
}

public class Assumption
{
    public required string Id { get; init; }
    public required string Text { get; set; }
    public string? ValidationStep { get; set; }
    public AssumptionStatus Status { get; set; } = AssumptionStatus.Unvalidated;
    public List<string> DerivedFrom { get; init; } = new();
}
