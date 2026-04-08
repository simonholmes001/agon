using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Infrastructure.Persistence.PostgreSQL;
using Agon.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Agon.Infrastructure.Tests.Persistence;

public sealed class TokenUsageRepositoryTests : IDisposable
{
    private readonly AgonDbContext _dbContext;
    private readonly ITokenUsageRepository _repository;

    public TokenUsageRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AgonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new AgonDbContext(options);
        _repository = new TokenUsageRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetWindowSummaryAsync_FiltersByUserAndWindow()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var windowStart = DateTimeOffset.UtcNow.AddDays(-1);
        var windowEnd = DateTimeOffset.UtcNow.AddDays(1);

        await _repository.AddRangeAsync(
            [
                BuildRecord(userA, sessionId, windowStart.AddHours(1), totalTokens: 100, promptTokens: 30, completionTokens: 70),
                BuildRecord(userA, sessionId, windowStart.AddHours(2), totalTokens: 40, promptTokens: 10, completionTokens: 30),
                BuildRecord(userA, sessionId, windowStart.AddDays(-3), totalTokens: 999, promptTokens: 999, completionTokens: 0),
                BuildRecord(userB, sessionId, windowStart.AddHours(3), totalTokens: 777, promptTokens: 500, completionTokens: 277)
            ],
            CancellationToken.None);

        var summary = await _repository.GetWindowSummaryAsync(userA, windowStart, windowEnd, CancellationToken.None);

        summary.TotalTokens.Should().Be(140);
        summary.PromptTokens.Should().Be(40);
        summary.CompletionTokens.Should().Be(100);
    }

    [Fact]
    public async Task ListByUserAndWindowAsync_ReturnsOrderedRecordsForRequestedWindow()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var windowStart = DateTimeOffset.UtcNow.AddDays(-1);
        var windowEnd = DateTimeOffset.UtcNow.AddDays(1);
        var first = windowStart.AddHours(1);
        var second = windowStart.AddHours(2);

        await _repository.AddRangeAsync(
            [
                BuildRecord(userId, sessionId, second, totalTokens: 50, promptTokens: 10, completionTokens: 40),
                BuildRecord(userId, sessionId, first, totalTokens: 25, promptTokens: 5, completionTokens: 20),
                BuildRecord(Guid.NewGuid(), sessionId, first, totalTokens: 999, promptTokens: 999, completionTokens: 0)
            ],
            CancellationToken.None);

        var records = await _repository.ListByUserAndWindowAsync(userId, windowStart, windowEnd, CancellationToken.None);

        records.Should().HaveCount(2);
        records[0].OccurredAt.Should().Be(first);
        records[1].OccurredAt.Should().Be(second);
        records.Select(r => r.TotalTokens).Should().Equal(25, 50);
    }

    [Fact]
    public async Task DeleteByUserAndWindowAsync_RemovesOnlyMatchingRecords()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var windowStart = DateTimeOffset.UtcNow.AddDays(-1);
        var windowEnd = DateTimeOffset.UtcNow.AddDays(1);

        await _repository.AddRangeAsync(
            [
                BuildRecord(userId, sessionId, windowStart.AddHours(1), totalTokens: 10, promptTokens: 2, completionTokens: 8),
                BuildRecord(userId, sessionId, windowStart.AddHours(2), totalTokens: 20, promptTokens: 4, completionTokens: 16),
                BuildRecord(userId, sessionId, windowStart.AddDays(-2), totalTokens: 30, promptTokens: 6, completionTokens: 24)
            ],
            CancellationToken.None);

        var deleted = await _repository.DeleteByUserAndWindowAsync(userId, windowStart, windowEnd, CancellationToken.None);
        var remaining = await _repository.ListByUserAndWindowAsync(userId, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);

        deleted.Should().Be(2);
        remaining.Should().HaveCount(1);
        remaining[0].TotalTokens.Should().Be(30);
    }

    private static TokenUsageRecord BuildRecord(
        Guid userId,
        Guid sessionId,
        DateTimeOffset occurredAt,
        int totalTokens,
        int promptTokens,
        int completionTokens)
    {
        return new TokenUsageRecord(
            Id: Guid.NewGuid(),
            UserId: userId,
            SessionId: sessionId,
            AgentId: "gpt_agent",
            Provider: "OpenAI",
            Model: "gpt-5",
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TotalTokens: totalTokens,
            Source: "provider",
            OccurredAt: occurredAt);
    }
}
