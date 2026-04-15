namespace Agon.Application.Models;

/// <summary>
/// Canonical extraction lifecycle states for uploaded attachments.
/// </summary>
public static class AttachmentExtractionStatus
{
    public const string Queued = "queued";
    public const string Extracting = "extracting";
    public const string Ready = "ready";
    public const string Failed = "failed";

    public static bool IsKnown(string value) =>
        value is Queued or Extracting or Ready or Failed;
}
