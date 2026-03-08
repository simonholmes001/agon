using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Infrastructure.Persistence.Entities;
using Agon.Infrastructure.Persistence.PostgreSQL;
using Microsoft.EntityFrameworkCore;

namespace Agon.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IAgentMessageRepository.
/// Per backend-implementation.instructions.md: Infrastructure layer implements repository interfaces.
/// </summary>
public sealed class AgentMessageRepository : IAgentMessageRepository
{
    private readonly AgonDbContext _dbContext;

    public AgentMessageRepository(AgonDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddAsync(AgentMessageRecord message, CancellationToken cancellationToken)
    {
        var entity = new AgentMessageEntity
        {
            Id = message.Id,
            SessionId = message.SessionId,
            AgentId = message.AgentId,
            Message = message.Message,
            Round = message.Round,
            CreatedAt = message.CreatedAt
        };

        await _dbContext.AgentMessages.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentMessageRecord>> GetBySessionIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var entities = await _dbContext.AgentMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities
            .Select(e => new AgentMessageRecord(
                e.Id,
                e.SessionId,
                e.AgentId,
                e.Message,
                e.Round,
                e.CreatedAt))
            .ToList();
    }
}
