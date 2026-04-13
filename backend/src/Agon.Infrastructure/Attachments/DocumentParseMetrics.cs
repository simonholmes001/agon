using System.Diagnostics.Metrics;

namespace Agon.Infrastructure.Attachments;

internal static class DocumentParseMetrics
{
    private static readonly Meter Meter = new("Agon.Infrastructure.DocumentParse");

    public static readonly Counter<long> ParseSuccess = Meter.CreateCounter<long>(
        "agon.document_parse.success",
        unit: "operations",
        description: "Number of successful canonical document.parse operations.");

    public static readonly Counter<long> ParseFailure = Meter.CreateCounter<long>(
        "agon.document_parse.failure",
        unit: "operations",
        description: "Number of failed canonical document.parse operations.");

    public static readonly Histogram<double> ParseDurationMs = Meter.CreateHistogram<double>(
        "agon.document_parse.duration.ms",
        unit: "ms",
        description: "End-to-end duration of canonical document.parse operations.");
}
