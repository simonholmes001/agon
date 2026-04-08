using Agon.Application.Models;

namespace Agon.Application.Interfaces;

/// <summary>
/// Persistence contract for model/provider token metering used by quota enforcement and reporting.
/// </summary>
public interface ITokenUsageRepository
{
    /// <summary>
    /// Persist a batch of token usage records.
    /// </summary>
    Task AddRangeAsync(IReadOnlyList<TokenUsageRecord> records, CancellationToken cancellationToken);

    /// <summary>
    /// Returns usage totals for a user within the inclusive/exclusive time window.
    /// </summary>
    Task<TokenUsageWindowSummary> GetWindowSummaryAsync(
        Guid userId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns raw usage records for a user within the inclusive/exclusive time window.
    /// </summary>
    Task<IReadOnlyList<TokenUsageRecord>> ListByUserAndWindowAsync(
        Guid userId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes usage records for a user within the inclusive/exclusive time window.
    /// Returns the number of deleted records.
    /// </summary>
    Task<int> DeleteByUserAndWindowAsync(
        Guid userId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken);
}
