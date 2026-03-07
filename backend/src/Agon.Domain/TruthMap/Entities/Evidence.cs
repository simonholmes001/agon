namespace Agon.Domain.TruthMap.Entities;

public sealed record Evidence(
    string Id,
    string Title,
    string Source,
    DateTimeOffset RetrievedAt,
    string Summary,
    IReadOnlyList<string> Supports,
    IReadOnlyList<string> Contradicts);
