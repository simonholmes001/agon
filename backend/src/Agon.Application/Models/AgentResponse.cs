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
    string? RawOutput)
{
    /// <summary>Creates a timed-out response stub for a specific agent.</summary>
    public static AgentResponse CreateTimedOut(string agentId) =>
        new(agentId, string.Empty, null, 0, true, null);

    public bool HasPatch => Patch is not null;
}
