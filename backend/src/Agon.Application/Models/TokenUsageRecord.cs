namespace Agon.Application.Models;

/// <summary>
/// Canonical persisted token-usage record for a single model/provider generation event.
/// </summary>
public sealed record TokenUsageRecord(
    Guid Id,
    Guid UserId,
    Guid SessionId,
    string AgentId,
    string Provider,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string Source,
    DateTimeOffset OccurredAt);

/// <summary>
/// Aggregated usage totals for a user within a specific reporting window.
/// </summary>
public sealed record TokenUsageWindowSummary(
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    long TotalTokens,
    long PromptTokens,
    long CompletionTokens);
