namespace Agon.Domain.TruthMap.Entities;

public sealed record OpenQuestion(
    string Id,
    string Text,
    bool Blocking,
    string RaisedBy);
