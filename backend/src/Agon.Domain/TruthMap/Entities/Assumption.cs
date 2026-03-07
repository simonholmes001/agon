namespace Agon.Domain.TruthMap.Entities;

public enum AssumptionStatus { Unvalidated, Validated, Invalidated }

public sealed record Assumption(
    string Id,
    string Text,
    string ValidationStep,
    IReadOnlyList<string> DerivedFrom,
    AssumptionStatus Status);
