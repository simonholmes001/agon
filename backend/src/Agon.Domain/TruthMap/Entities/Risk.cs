namespace Agon.Domain.TruthMap.Entities;

public enum RiskCategory
{
    Market,
    Technical,
    Execution,
    Security,
    Financial
}

public enum Severity
{
    Low,
    Medium,
    High,
    Critical
}

public enum Likelihood
{
    Low,
    Medium,
    High
}

public class Risk
{
    public required string Id { get; init; }
    public required string Text { get; set; }
    public RiskCategory Category { get; set; }
    public Severity Severity { get; set; }
    public Likelihood Likelihood { get; set; }
    public string Mitigation { get; set; } = string.Empty;
    public required string Agent { get; init; }
    public List<string> DerivedFrom { get; init; } = new();
}
