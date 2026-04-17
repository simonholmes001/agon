using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Orchestration;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Agon.Application.Services;

/// <summary>
/// Service for managing conversation history (agent messages and user responses).
/// Per copilot.instructions.md: Application services orchestrate use-cases.
/// </summary>
public sealed class ConversationHistoryService
{
    private sealed record StageTiming(string Phase, DateTimeOffset StartedAt);

    private static readonly Regex StageRegex = new(
        @"\bstage\s*:\s*([a-z_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReasonCodeRegex = new(
        @"\bReasonCode=([A-Z0-9_]+)",
        RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<Guid, StageTiming> StageTimings = new();

    private readonly IAgentMessageRepository _messageRepo;
    private readonly ISessionRepository? _sessionRepo;

    public ConversationHistoryService(
        IAgentMessageRepository messageRepo,
        ISessionRepository? sessionRepo = null)
    {
        _messageRepo = messageRepo ?? throw new ArgumentNullException(nameof(messageRepo));
        _sessionRepo = sessionRepo;
    }

    /// <summary>
    /// Stores an agent message in the conversation history.
    /// </summary>
    public async Task StoreMessageAsync(
        Guid sessionId,
        string agentId,
        string message,
        int round,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message cannot be null or empty", nameof(message));

        var record = new AgentMessageRecord(
            Id: Guid.NewGuid(),
            SessionId: sessionId,
            AgentId: agentId,
            Message: message,
            Round: round,
            CreatedAt: DateTimeOffset.UtcNow);

        await _messageRepo.AddAsync(record, cancellationToken);

        await TryPersistCouncilRunStateAsync(sessionId, agentId, message, cancellationToken);
    }

    /// <summary>
    /// Retrieves all messages for a session in chronological order.
    /// </summary>
    public async Task<IReadOnlyList<AgentMessageRecord>> GetMessagesAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return await _messageRepo.GetBySessionIdAsync(sessionId, cancellationToken);
    }

    private async Task TryPersistCouncilRunStateAsync(
        Guid sessionId,
        string agentId,
        string message,
        CancellationToken cancellationToken)
    {
        if (_sessionRepo is null)
        {
            return;
        }

        var normalizedAgentId = agentId.Trim().ToLowerInvariant();
        var isCouncilRunEvent = normalizedAgentId is "council_running"
            or "council_progress"
            or "council_failed"
            or "council_complete";
        if (!isCouncilRunEvent)
        {
            return;
        }

        var state = await _sessionRepo.GetAsync(sessionId, cancellationToken);
        if (state is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        if (normalizedAgentId == "council_running")
        {
            state.CouncilRunStartedAt = now;
            state.CouncilRunPhase = "queued";
            state.CouncilRunFirstProgressAt = null;
            state.CouncilRunLastProgressAt = now;
            state.CouncilRunCompletedAt = null;
            state.CouncilRunFailedReason = null;
            StageTimings[sessionId] = new StageTiming("queued", now);
            CouncilRunMetrics.RunStarted.Add(1);
        }
        else if (normalizedAgentId == "council_progress")
        {
            if (state.CouncilRunStartedAt is null)
            {
                state.CouncilRunStartedAt = now;
                CouncilRunMetrics.RunStarted.Add(1);
            }

            if (state.CouncilRunFirstProgressAt is null)
            {
                state.CouncilRunFirstProgressAt = now;
                CouncilRunMetrics.FirstProgress.Add(1);
                if (state.CouncilRunStartedAt.HasValue)
                {
                    CouncilRunMetrics.TimeToFirstProgressMs.Record(
                        Math.Max(0, (now - state.CouncilRunStartedAt.Value).TotalMilliseconds));
                }
            }

            var previousPhase = state.CouncilRunPhase;
            var currentStage = ExtractStage(message);
            var phaseChanged = !string.Equals(previousPhase, currentStage, StringComparison.OrdinalIgnoreCase);

            if (phaseChanged
                && StageTimings.TryGetValue(sessionId, out var priorTiming)
                && !string.IsNullOrWhiteSpace(priorTiming.Phase))
            {
                RecordStageDuration(priorTiming.Phase, now - priorTiming.StartedAt);
            }

            if (phaseChanged
                || IsStageStartingMessage(message)
                || !StageTimings.TryGetValue(sessionId, out var activeTiming)
                || !string.Equals(activeTiming.Phase, currentStage, StringComparison.OrdinalIgnoreCase))
            {
                StageTimings[sessionId] = new StageTiming(currentStage, now);
            }

            if (IsStageCompletedMessage(message)
                && StageTimings.TryGetValue(sessionId, out var completedTiming)
                && string.Equals(completedTiming.Phase, currentStage, StringComparison.OrdinalIgnoreCase))
            {
                RecordStageDuration(currentStage, now - completedTiming.StartedAt);
                StageTimings.TryRemove(sessionId, out _);
            }

            state.CouncilRunPhase = currentStage;
            state.CouncilRunLastProgressAt = now;
            state.CouncilRunCompletedAt = null;
            state.CouncilRunFailedReason = null;
        }
        else if (normalizedAgentId == "council_complete")
        {
            if (state.CouncilRunStartedAt is null)
            {
                state.CouncilRunStartedAt = now;
                CouncilRunMetrics.RunStarted.Add(1);
            }

            if (StageTimings.TryGetValue(sessionId, out var lastStageTiming))
            {
                RecordStageDuration(lastStageTiming.Phase, now - lastStageTiming.StartedAt);
            }
            StageTimings.TryRemove(sessionId, out _);

            state.CouncilRunPhase = "completed";
            state.CouncilRunLastProgressAt = now;
            state.CouncilRunCompletedAt = now;
            state.CouncilRunFailedReason = null;
            CouncilRunMetrics.RunCompleted.Add(1);
            if (state.CouncilRunStartedAt.HasValue)
            {
                CouncilRunMetrics.RunDurationMs.Record(
                    Math.Max(0, (now - state.CouncilRunStartedAt.Value).TotalMilliseconds));
            }
        }
        else
        {
            if (state.CouncilRunStartedAt is null)
            {
                state.CouncilRunStartedAt = now;
                CouncilRunMetrics.RunStarted.Add(1);
            }

            if (StageTimings.TryGetValue(sessionId, out var failedStageTiming))
            {
                RecordStageDuration(failedStageTiming.Phase, now - failedStageTiming.StartedAt);
            }
            StageTimings.TryRemove(sessionId, out _);

            state.CouncilRunPhase = "failed";
            state.CouncilRunLastProgressAt = now;
            state.CouncilRunCompletedAt = now;
            state.CouncilRunFailedReason = ExtractReasonCode(message);
            CouncilRunMetrics.RunFailed.Add(
                1,
                new KeyValuePair<string, object?>("reason_code", state.CouncilRunFailedReason ?? "UNKNOWN"));
            if (state.CouncilRunStartedAt.HasValue)
            {
                CouncilRunMetrics.RunDurationMs.Record(
                    Math.Max(0, (now - state.CouncilRunStartedAt.Value).TotalMilliseconds));
            }
        }

        await _sessionRepo.UpdateCouncilRunMetadataAsync(state, cancellationToken);
    }

    private static string ExtractStage(string message)
    {
        var match = StageRegex.Match(message);
        if (!match.Success)
        {
            return "analysis";
        }

        return match.Groups[1].Value.Trim().ToLowerInvariant();
    }

    private static string? ExtractReasonCode(string message)
    {
        var match = ReasonCodeRegex.Match(message);
        return match.Success ? match.Groups[1].Value.Trim().ToUpperInvariant() : null;
    }

    private static bool IsStageStartingMessage(string message) =>
        message.Contains("(starting)", StringComparison.OrdinalIgnoreCase);

    private static bool IsStageCompletedMessage(string message) =>
        message.Contains("(completed)", StringComparison.OrdinalIgnoreCase);

    private static void RecordStageDuration(string phase, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            return;
        }

        CouncilRunMetrics.StageDurationMs.Record(
            Math.Max(0, duration.TotalMilliseconds),
            new KeyValuePair<string, object?>("stage", phase));
    }
}
