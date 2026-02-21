using Agon.Domain.TruthMap;

namespace Agon.Application.Orchestration;

public class AgentExecutionResult
{
    public required string AgentId { get; init; }
    public bool TimedOut { get; init; }
    public TruthMapPatch? Patch { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Error { get; init; }

    public static AgentExecutionResult Success(string agentId, TruthMapPatch? patch, string message = "") =>
        new()
        {
            AgentId = agentId,
            TimedOut = false,
            Patch = patch,
            Message = message
        };

    public static AgentExecutionResult Timeout(string agentId) =>
        new()
        {
            AgentId = agentId,
            TimedOut = true,
            Patch = null,
            Message = string.Empty,
            Error = "timeout"
        };

    public static AgentExecutionResult Failed(string agentId, string error) =>
        new()
        {
            AgentId = agentId,
            TimedOut = false,
            Patch = null,
            Message = string.Empty,
            Error = error
        };
}
