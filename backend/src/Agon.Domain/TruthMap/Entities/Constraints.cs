namespace Agon.Domain.TruthMap.Entities;

public sealed record Constraints(
    string Budget,
    string Timeline,
    IReadOnlyList<string> TechStack,
    IReadOnlyList<string> NonNegotiables);
