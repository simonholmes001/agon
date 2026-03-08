using Agon.Application.Interfaces;
using Agon.Application.Models;

namespace Agon.Application.Services;

/// <summary>
/// Service for managing conversation history (agent messages and user responses).
/// Per copilot.instructions.md: Application services orchestrate use-cases.
/// </summary>
public sealed class ConversationHistoryService
{
    private readonly IAgentMessageRepository _messageRepo;

    public ConversationHistoryService(IAgentMessageRepository messageRepo)
    {
        _messageRepo = messageRepo ?? throw new ArgumentNullException(nameof(messageRepo));
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
}
