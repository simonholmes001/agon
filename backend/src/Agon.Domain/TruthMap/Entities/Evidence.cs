namespace Agon.Domain.TruthMap.Entities;

public class Evidence
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Source { get; init; }
    public DateTimeOffset RetrievedAt { get; init; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Supports { get; init; } = new();
    public List<string> Contradicts { get; init; } = new();
}
