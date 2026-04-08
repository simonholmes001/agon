using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Infrastructure.Persistence.Entities;
using Agon.Infrastructure.Persistence.PostgreSQL;
using Microsoft.EntityFrameworkCore;

namespace Agon.Infrastructure.Persistence.Repositories;

public sealed class TokenUsageRepository : ITokenUsageRepository
{
    private readonly AgonDbContext _dbContext;

    public TokenUsageRepository(AgonDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddRangeAsync(IReadOnlyList<TokenUsageRecord> records, CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return;
        }

        var entities = records.Select(record => new TokenUsageRecordEntity
        {
            Id = record.Id,
            UserId = record.UserId,
            SessionId = record.SessionId,
            AgentId = record.AgentId,
            Provider = record.Provider,
            Model = record.Model,
            PromptTokens = record.PromptTokens,
            CompletionTokens = record.CompletionTokens,
            TotalTokens = record.TotalTokens,
            Source = record.Source,
            OccurredAt = record.OccurredAt
        });

        await _dbContext.TokenUsageRecords.AddRangeAsync(entities, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<TokenUsageWindowSummary> GetWindowSummaryAsync(
        Guid userId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.TokenUsageRecords
            .Where(record =>
                record.UserId == userId
                && record.OccurredAt >= windowStart
                && record.OccurredAt < windowEnd);

        var totals = await query
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalTokens = group.Sum(record => (long)record.TotalTokens),
                PromptTokens = group.Sum(record => (long)record.PromptTokens),
                CompletionTokens = group.Sum(record => (long)record.CompletionTokens)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return totals is null
            ? new TokenUsageWindowSummary(windowStart, windowEnd, 0, 0, 0)
            : new TokenUsageWindowSummary(
                windowStart,
                windowEnd,
                totals.TotalTokens,
                totals.PromptTokens,
                totals.CompletionTokens);
    }

    public async Task<IReadOnlyList<TokenUsageRecord>> ListByUserAndWindowAsync(
        Guid userId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
    {
        var entities = await _dbContext.TokenUsageRecords
            .Where(record =>
                record.UserId == userId
                && record.OccurredAt >= windowStart
                && record.OccurredAt < windowEnd)
            .OrderBy(record => record.OccurredAt)
            .ToListAsync(cancellationToken);

        return entities.Select(entity => new TokenUsageRecord(
            entity.Id,
            entity.UserId,
            entity.SessionId,
            entity.AgentId,
            entity.Provider,
            entity.Model,
            entity.PromptTokens,
            entity.CompletionTokens,
            entity.TotalTokens,
            entity.Source,
            entity.OccurredAt)).ToList();
    }

    public async Task<int> DeleteByUserAndWindowAsync(
        Guid userId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
    {
        var toDelete = await _dbContext.TokenUsageRecords
            .Where(record =>
                record.UserId == userId
                && record.OccurredAt >= windowStart
                && record.OccurredAt < windowEnd)
            .ToListAsync(cancellationToken);

        if (toDelete.Count == 0)
        {
            return 0;
        }

        _dbContext.TokenUsageRecords.RemoveRange(toDelete);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return toDelete.Count;
    }
}
