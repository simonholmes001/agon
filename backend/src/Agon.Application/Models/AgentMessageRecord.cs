namespace Agon.Application.Models;

/// <summary>
/// Represents a persisted agent message in a session's conversation history.
/// Includes both agent MESSAGEs and user responses.
/// </summary>
public sealed record AgentMessageRecord(
    Guid Id,
    Guid SessionId,
    string AgentId,
    string Message,
    int Round,
    DateTimeOffset CreatedAt);
