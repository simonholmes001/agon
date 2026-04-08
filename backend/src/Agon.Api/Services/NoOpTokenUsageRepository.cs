using Agon.Application.Interfaces;
using Agon.Application.Models;

namespace Agon.Api.Services;

/// <summary>
/// Safe fallback token-usage repository used when persistence is not configured
/// (for example, local dev/test modes without PostgreSQL wiring).
/// </summary>
public sealed class NoOpTokenUsageRepository : ITokenUsageRepository
{
    public Task AddRangeAsync(IReadOnlyList<TokenUsageRecord> records, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<TokenUsageWindowSummary> GetWindowSummaryAsync(
        Guid userId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
        => Task.FromResult(new TokenUsageWindowSummary(windowStart, windowEnd, 0, 0, 0));

    public Task<IReadOnlyList<TokenUsageRecord>> ListByUserAndWindowAsync(
        Guid userId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TokenUsageRecord>>(Array.Empty<TokenUsageRecord>());

    public Task<int> DeleteByUserAndWindowAsync(
        Guid userId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
        => Task.FromResult(0);
}
