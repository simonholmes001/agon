using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Domain.Sessions;
using Agon.Application.Orchestration;
using System.Text.RegularExpressions;

namespace Agon.Application.Services;

/// <summary>
/// Service for managing conversation history (agent messages and user responses).
/// Per copilot.instructions.md: Application services orchestrate use-cases.
/// </summary>
public sealed class ConversationHistoryService
{
    private static readonly Regex StageRegex = new(
        @"\bstage\s*:\s*([a-z_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReasonCodeRegex = new(
        @"\bReasonCode=([A-Z0-9_]+)",
        RegexOptions.Compiled);

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
        var previousPhase = state.CouncilRunPhase;
        var previousLastProgress = state.CouncilRunLastProgressAt;

        if (normalizedAgentId == "council_running")
        {
            if (state.CouncilRunStartedAt is null)
            {
                state.CouncilRunStartedAt = now;
                CouncilRunMetrics.RunStarted.Add(1);
            }

            state.CouncilRunPhase = "queued";
            state.CouncilRunFirstProgressAt = null;
            state.CouncilRunLastProgressAt = now;
            state.CouncilRunCompletedAt = null;
            state.CouncilRunFailedReason = null;
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

            state.CouncilRunPhase = ExtractStage(message);
            state.CouncilRunLastProgressAt = now;
        }
        else if (normalizedAgentId == "council_complete")
        {
            if (state.CouncilRunStartedAt is null)
            {
                state.CouncilRunStartedAt = now;
                CouncilRunMetrics.RunStarted.Add(1);
            }

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

            state.Status = SessionStatus.CompleteWithGaps;
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

        if (!string.Equals(previousPhase, state.CouncilRunPhase, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(previousPhase)
            && previousLastProgress.HasValue)
        {
            CouncilRunMetrics.StageDurationMs.Record(
                Math.Max(0, (now - previousLastProgress.Value).TotalMilliseconds),
                new KeyValuePair<string, object?>("stage", previousPhase));
        }

        await _sessionRepo.UpdateAsync(state, cancellationToken);
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
}
