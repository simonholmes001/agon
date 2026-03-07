namespace Agon.Api.Configuration;

/// <summary>
/// Agon-specific configuration loaded from appsettings.json
/// </summary>
public sealed class AgonConfiguration
{
    public const string SectionName = "Agon";

    public int DefaultFrictionLevel { get; set; } = 50;
    public int MaxClarificationRounds { get; set; } = 2;
    public int MaxDebateRounds { get; set; } = 2;
    public int MaxTargetedLoops { get; set; } = 2;
    public int SessionBudgetTokens { get; set; } = 50000;
    public double ConvergenceThreshold { get; set; } = 0.75;
    public int HighFrictionThreshold { get; set; } = 70;
}
