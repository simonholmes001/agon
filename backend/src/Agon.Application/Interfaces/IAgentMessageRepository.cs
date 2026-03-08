using Agon.Application.Models;

namespace Agon.Application.Interfaces;

/// <summary>
/// Repository for persisted agent messages (conversation history).
/// Per architecture.instructions.md: Application layer defines interfaces,
/// Infrastructure layer implements them.
/// </summary>
public interface IAgentMessageRepository
{
    /// <summary>
    /// Stores a new agent message in the conversation history.
    /// </summary>
    Task AddAsync(AgentMessageRecord message, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all messages for a session in chronological order.
    /// </summary>
    Task<IReadOnlyList<AgentMessageRecord>> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken);
}
