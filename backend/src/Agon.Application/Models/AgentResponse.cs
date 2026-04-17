using Agon.Domain.TruthMap;

namespace Agon.Application.Models;

/// <summary>
/// The output of a single agent call. Contains the human-readable MESSAGE section,
/// the structured PATCH, and raw output for debugging.
/// </summary>
public sealed record AgentResponse(
    string AgentId,
    string Message,
    TruthMapPatch? Patch,
    int TokensUsed,
    bool TimedOut,
    string? RawOutput,
    int PromptTokens = 0,
    int CompletionTokens = 0,
    string TokenUsageSource = "estimated",
    bool Failed = false,
    string? FailureReason = null)
{
    /// <summary>Creates a timed-out response stub for a specific agent.</summary>
    public static AgentResponse CreateTimedOut(string agentId) =>
        new(agentId, string.Empty, null, 0, true, null);

    /// <summary>Creates a non-timeout failure response stub for a specific agent.</summary>
    public static AgentResponse CreateFailed(string agentId, string? failureReason = null) =>
        new(agentId, string.Empty, null, 0, false, null, Failed: true, FailureReason: failureReason);

    public bool HasPatch => Patch is not null;
}
