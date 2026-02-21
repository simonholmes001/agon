namespace Agon.Domain.TruthMap.Entities;

public class Decision
{
    public required string Id { get; init; }
    public required string Text { get; set; }
    public required string Rationale { get; set; }
    public required string Owner { get; init; }
    public bool Binding { get; set; }
    public List<string> DerivedFrom { get; init; } = new();
}
