namespace Agon.Application.Sessions;

public enum TranscriptMessageType
{
    Agent = 0,
    User = 1,
    System = 2
}

public class TranscriptMessage
{
    public Guid Id { get; init; }

    public Guid SessionId { get; init; }

    public TranscriptMessageType Type { get; init; }

    public string? AgentId { get; init; }

    public string Content { get; init; } = string.Empty;

    public int Round { get; init; }

    public bool IsStreaming { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
