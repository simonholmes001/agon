using System.Diagnostics.Metrics;

namespace Agon.Application.Orchestration;

internal static class AttachmentChunkLoopMetrics
{
    private static readonly Meter Meter = new("Agon.Application.AttachmentChunkLoop");

    public static readonly Counter<long> Activations = Meter.CreateCounter<long>(
        "agon.attachment_chunk_loop.activations",
        unit: "operations",
        description: "Number of analysis rounds that activated attachment chunk-loop processing.");

    public static readonly Counter<long> ChunkedAttachments = Meter.CreateCounter<long>(
        "agon.attachment_chunk_loop.attachments",
        unit: "attachments",
        description: "Number of attachments processed via chunk-loop.");

    public static readonly Counter<long> Passes = Meter.CreateCounter<long>(
        "agon.attachment_chunk_loop.passes",
        unit: "passes",
        description: "Number of chunk-loop passes executed.");

    public static readonly Counter<long> Responses = Meter.CreateCounter<long>(
        "agon.attachment_chunk_loop.responses",
        unit: "responses",
        description: "Number of agent responses captured during chunk-loop passes.");

    public static readonly Counter<long> NotesGenerated = Meter.CreateCounter<long>(
        "agon.attachment_chunk_loop.notes_generated",
        unit: "notes",
        description: "Number of chunk-loop notes retained for final synthesis.");

    public static readonly Histogram<double> PreludeDurationMs = Meter.CreateHistogram<double>(
        "agon.attachment_chunk_loop.prelude.duration.ms",
        unit: "ms",
        description: "Duration of attachment chunk-loop prelude processing.");
}
