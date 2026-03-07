namespace Agon.Domain.TruthMap.Entities;

public enum RiskCategory { Market, Technical, Operational, Financial, Regulatory }
public enum RiskSeverity { Low, Medium, High, Critical }
public enum RiskLikelihood { Low, Medium, High }

public sealed record Risk(
    string Id,
    string Text,
    RiskCategory Category,
    RiskSeverity Severity,
    RiskLikelihood Likelihood,
    string Mitigation,
    IReadOnlyList<string> DerivedFrom,
    string RaisedBy);
