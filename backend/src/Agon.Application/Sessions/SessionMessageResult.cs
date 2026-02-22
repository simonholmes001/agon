namespace Agon.Application.Sessions;

public sealed record SessionMessageResult(
    Guid SessionId,
    string Phase,
    string RoutedAgentId,
    string Reply,
    bool PatchApplied);
