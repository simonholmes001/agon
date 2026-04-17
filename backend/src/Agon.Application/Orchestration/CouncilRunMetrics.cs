using System.Diagnostics.Metrics;

namespace Agon.Application.Orchestration;

internal static class CouncilRunMetrics
{
    private static readonly Meter Meter = new("Agon.Application.CouncilRuns");

    public static readonly Counter<long> RunStarted = Meter.CreateCounter<long>(
        "agon_council_run_started_total",
        unit: "{run}",
        description: "Total number of council runs started.");

    public static readonly Counter<long> FirstProgress = Meter.CreateCounter<long>(
        "agon_council_run_first_progress_total",
        unit: "{run}",
        description: "Total number of council runs reaching first progress.");

    public static readonly Counter<long> RunCompleted = Meter.CreateCounter<long>(
        "agon_council_run_completed_total",
        unit: "{run}",
        description: "Total number of council runs completed.");

    public static readonly Counter<long> RunFailed = Meter.CreateCounter<long>(
        "agon_council_run_failed_total",
        unit: "{run}",
        description: "Total number of council runs failed.");

    public static readonly Histogram<double> TimeToFirstProgressMs = Meter.CreateHistogram<double>(
        "agon_council_time_to_first_progress_ms",
        unit: "ms",
        description: "Council run latency from start to first progress.");

    public static readonly Histogram<double> RunDurationMs = Meter.CreateHistogram<double>(
        "agon_council_run_duration_ms",
        unit: "ms",
        description: "Council run duration from start to terminal state.");

    public static readonly Histogram<double> StageDurationMs = Meter.CreateHistogram<double>(
        "agon_council_stage_duration_ms",
        unit: "ms",
        description: "Observed duration spent in each council stage.");
}
