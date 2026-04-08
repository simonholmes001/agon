namespace Agon.Api.Configuration;

/// <summary>
/// Runtime configuration for invite-only MVP trial controls.
/// </summary>
public sealed class TrialAccessConfiguration
{
    public const string SectionName = "TrialAccess";

    /// <summary>
    /// Enables MVP trial controls (allowlist, quota, throttling).
    /// Keep false for local development unless explicitly testing trial behavior.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Default trial duration used by admin grant operations when an explicit expiry is not provided.
    /// </summary>
    public int DefaultTrialDays { get; set; } = 7;

    /// <summary>
    /// Static admin key required for trial admin endpoints (X-Agon-Admin-Key).
    /// </summary>
    public string AdminApiKey { get; set; } = string.Empty;

    public TrialQuotaConfiguration Quota { get; set; } = new();

    public TrialRequestRateLimitConfiguration RequestRateLimit { get; set; } = new();
}

public sealed class TrialQuotaConfiguration
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Hard per-user token budget for each quota window.
    /// </summary>
    public int TokenLimit { get; set; } = 150_000;

    /// <summary>
    /// Quota window size in days. MVP default is one week.
    /// </summary>
    public int WindowDays { get; set; } = 7;
}

public sealed class TrialRequestRateLimitConfiguration
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Allowed requests per minute for each user.
    /// </summary>
    public int RequestsPerMinute { get; set; } = 20;

    /// <summary>
    /// Maximum immediate burst size for each user.
    /// </summary>
    public int BurstCapacity { get; set; } = 10;
}
