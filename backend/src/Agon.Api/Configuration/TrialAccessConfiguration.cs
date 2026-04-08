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

    /// <summary>
    /// Rollout mode for runtime access decisions.
    /// RestrictedGroups: require tester-group membership during early access.
    /// AllAuthenticatedUsers: allow any authenticated user (post-early-testers mode).
    /// </summary>
    public string AccessMode { get; set; } = TrialAccessModes.RestrictedGroups;

    /// <summary>
    /// When enabled, users must present at least one configured Entra group in their token claims.
    /// </summary>
    public bool EnforceEntraGroupMembership { get; set; } = true;

    /// <summary>
    /// Canonical Entra group object IDs that grant trial tester access.
    /// </summary>
    public List<string> RequiredEntraGroupObjectIds { get; set; } = [];

    /// <summary>
    /// Optional comma-separated Entra group object IDs for environments where array-style app settings
    /// are harder to manage.
    /// </summary>
    public string RequiredEntraGroupObjectIdsCsv { get; set; } = string.Empty;

    /// <summary>
    /// Canonical Entra group object IDs that bypass trial controls for operators/admins.
    /// </summary>
    public List<string> AdminBypassEntraGroupObjectIds { get; set; } = [];

    /// <summary>
    /// Optional comma-separated Entra group object IDs for admin bypass users.
    /// </summary>
    public string AdminBypassEntraGroupObjectIdsCsv { get; set; } = string.Empty;

    public TrialQuotaConfiguration Quota { get; set; } = new();

    public TrialRequestRateLimitConfiguration RequestRateLimit { get; set; } = new();
}

public static class TrialAccessModes
{
    public const string RestrictedGroups = "RestrictedGroups";
    public const string AllAuthenticatedUsers = "AllAuthenticatedUsers";
}

public sealed class TrialQuotaConfiguration
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Hard per-user token budget for each quota window.
    /// </summary>
    public int TokenLimit { get; set; } = 40_000;

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
