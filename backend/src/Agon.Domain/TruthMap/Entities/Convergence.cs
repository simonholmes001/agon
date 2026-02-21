namespace Agon.Domain.TruthMap.Entities;

public enum ConvergenceStatus
{
    InProgress,
    Converged,
    GapsRemain
}

public class Convergence
{
    public float ClaritySpecificity { get; set; }
    public float Feasibility { get; set; }
    public float RiskCoverage { get; set; }
    public float AssumptionExplicitness { get; set; }
    public float Coherence { get; set; }
    public float Actionability { get; set; }
    public float EvidenceQuality { get; set; }
    public float Overall { get; set; }
    public float Threshold { get; set; }
    public ConvergenceStatus Status { get; set; } = ConvergenceStatus.InProgress;
}
