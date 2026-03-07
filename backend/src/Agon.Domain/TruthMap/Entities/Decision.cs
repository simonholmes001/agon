namespace Agon.Domain.TruthMap.Entities;

public sealed record Decision(
    string Id,
    string Text,
    string Rationale,
    string Owner,
    IReadOnlyList<string> DerivedFrom,
    bool Binding);
