namespace Agon.Api.Contracts;

/// <summary>
/// Request to create a new debate session.
/// </summary>
/// <param name="Idea">The user's idea to analyse (minimum 10 characters).</param>
/// <param name="Mode">Session mode: "quick" or "deep".</param>
/// <param name="FrictionLevel">Friction level 0-100 controlling critique intensity.</param>
public record CreateSessionRequest(string Idea, string Mode, int FrictionLevel);

/// <summary>
/// Request to post a user message to an active session.
/// </summary>
/// <param name="Message">The user's message content.</param>
public record PostSessionMessageRequest(string? Message);

/// <summary>
/// Response containing session state.
/// </summary>
public record SessionResponse(
    Guid SessionId,
    string Status,
    string Phase,
    string Mode,
    int FrictionLevel,
    int RoundNumber,
    int TargetedLoopCount);

/// <summary>
/// Response containing Truth Map state.
/// </summary>
public record TruthMapResponse(
    Guid SessionId,
    int Version,
    int Round,
    string CoreIdea);

/// <summary>
/// Response containing a single transcript message.
/// </summary>
public record TranscriptMessageResponse(
    Guid Id,
    string Type,
    string? AgentId,
    string Content,
    int Round,
    bool IsStreaming,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Response after posting a user message.
/// </summary>
public record SessionMessageResponse(
    Guid SessionId,
    string Phase,
    string RoutedAgentId,
    string Reply,
    bool PatchApplied);
