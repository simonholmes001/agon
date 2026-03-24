using System.Diagnostics.Metrics;

namespace Agon.Api.Observability;

internal static class AttachmentMetrics
{
    private static readonly Meter Meter = new("Agon.Api.Attachments", "1.0.0");

    internal static readonly Counter<long> UploadSuccess = Meter.CreateCounter<long>(
        "agon.attachments.upload.success",
        unit: "requests",
        description: "Attachment upload requests that completed successfully.");

    internal static readonly Counter<long> UploadFailure = Meter.CreateCounter<long>(
        "agon.attachments.upload.failure",
        unit: "requests",
        description: "Attachment upload requests that failed.");

    internal static readonly Counter<long> ExtractionFailure = Meter.CreateCounter<long>(
        "agon.attachments.extraction.failure",
        unit: "requests",
        description: "Attachment extraction attempts that failed.");

    internal static readonly Counter<long> RetentionDeleted = Meter.CreateCounter<long>(
        "agon.attachments.retention.deleted",
        unit: "attachments",
        description: "Attachment records deleted by retention cleanup.");

    internal static readonly Counter<long> RetentionFailed = Meter.CreateCounter<long>(
        "agon.attachments.retention.failed",
        unit: "attachments",
        description: "Attachment cleanup attempts that failed.");

    internal static readonly Counter<long> RateLimitRejected = Meter.CreateCounter<long>(
        "agon.api.ratelimit.rejected",
        unit: "requests",
        description: "Requests rejected by API rate limiting.");
}
