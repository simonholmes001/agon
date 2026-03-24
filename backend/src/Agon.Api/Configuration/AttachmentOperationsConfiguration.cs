namespace Agon.Api.Configuration;

/// <summary>
/// Runtime controls for attachment access URLs and retention cleanup.
/// </summary>
public sealed class AttachmentOperationsConfiguration
{
    public const string SectionName = "AttachmentOperations";

    public AttachmentRetentionConfiguration Retention { get; set; } = new();
}

public sealed class AttachmentRetentionConfiguration
{
    public bool CleanupEnabled { get; set; } = true;

    /// <summary>
    /// Number of days attachments are kept before cleanup is eligible.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Background cleanup execution cadence.
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum attachments deleted per sweep.
    /// </summary>
    public int CleanupBatchSize { get; set; } = 100;
}
