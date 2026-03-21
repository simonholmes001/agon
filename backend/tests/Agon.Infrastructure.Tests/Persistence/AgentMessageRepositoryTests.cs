using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Infrastructure.Persistence.PostgreSQL;
using Agon.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Agon.Infrastructure.Tests.Persistence;

/// <summary>
/// Unit tests for AgentMessageRepository using in-memory database.
/// </summary>
public class AgentMessageRepositoryTests : IDisposable
{
    private readonly AgonDbContext _dbContext;
    private readonly IAgentMessageRepository _repository;

    public AgentMessageRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AgonDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new AgonDbContext(options);
        _repository = new AgentMessageRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task AddAsync_StoresMessageInDatabase()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var message = new AgentMessageRecord(
            messageId,
            sessionId,
            "gpt_agent",
            "## MESSAGE\nThis is a test message",
            Round: 1,
            CreatedAt: DateTimeOffset.UtcNow);

        // Act
        await _repository.AddAsync(message, CancellationToken.None);

        // Assert
        var entity = await _dbContext.AgentMessages.FirstOrDefaultAsync(m => m.Id == messageId);
        entity.Should().NotBeNull();
        entity!.SessionId.Should().Be(sessionId);
        entity.AgentId.Should().Be("gpt_agent");
        entity.Message.Should().Be("## MESSAGE\nThis is a test message");
        entity.Round.Should().Be(1);
    }

    [Fact]
    public async Task GetBySessionIdAsync_WhenNoMessages_ReturnsEmptyList()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act
        var messages = await _repository.GetBySessionIdAsync(sessionId, CancellationToken.None);

        // Assert
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBySessionIdAsync_WhenMessagesExist_ReturnsSortedByCreatedAt()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var baseTime = DateTimeOffset.UtcNow;

        var msg1 = new AgentMessageRecord(Guid.NewGuid(), sessionId, "gpt_agent", "Message 1", 1, baseTime.AddMinutes(-5));
        var msg2 = new AgentMessageRecord(Guid.NewGuid(), sessionId, "gemini_agent", "Message 2", 1, baseTime.AddMinutes(-3));
        var msg3 = new AgentMessageRecord(Guid.NewGuid(), sessionId, "claude_agent", "Message 3", 1, baseTime.AddMinutes(-1));

        // Add in reverse order to confirm sorting
        await _repository.AddAsync(msg3, CancellationToken.None);
        await _repository.AddAsync(msg1, CancellationToken.None);
        await _repository.AddAsync(msg2, CancellationToken.None);

        // Act
        var messages = await _repository.GetBySessionIdAsync(sessionId, CancellationToken.None);

        // Assert
        messages.Should().HaveCount(3);
        messages[0].AgentId.Should().Be("gpt_agent");    // earliest
        messages[1].AgentId.Should().Be("gemini_agent");
        messages[2].AgentId.Should().Be("claude_agent"); // latest
    }

    [Fact]
    public async Task GetBySessionIdAsync_OnlyReturnsMessagesForSpecifiedSession()
    {
        // Arrange
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        var msg1 = new AgentMessageRecord(Guid.NewGuid(), session1, "gpt_agent", "Message for session 1", 1, DateTimeOffset.UtcNow);
        var msg2 = new AgentMessageRecord(Guid.NewGuid(), session2, "gpt_agent", "Message for session 2", 1, DateTimeOffset.UtcNow);

        await _repository.AddAsync(msg1, CancellationToken.None);
        await _repository.AddAsync(msg2, CancellationToken.None);

        // Act
        var messages = await _repository.GetBySessionIdAsync(session1, CancellationToken.None);

        // Assert
        messages.Should().HaveCount(1);
        messages[0].SessionId.Should().Be(session1);
        messages[0].Message.Should().Be("Message for session 1");
    }

    [Fact]
    public async Task GetBySessionIdAsync_CorrectlyMapsAllFields()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var message = new AgentMessageRecord(messageId, sessionId, "claude_agent", "Agent analysis here", 3, createdAt);

        await _repository.AddAsync(message, CancellationToken.None);

        // Act
        var messages = await _repository.GetBySessionIdAsync(sessionId, CancellationToken.None);

        // Assert
        messages.Should().HaveCount(1);
        var retrieved = messages[0];
        retrieved.Id.Should().Be(messageId);
        retrieved.SessionId.Should().Be(sessionId);
        retrieved.AgentId.Should().Be("claude_agent");
        retrieved.Message.Should().Be("Agent analysis here");
        retrieved.Round.Should().Be(3);
        retrieved.CreatedAt.Should().BeCloseTo(createdAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AddAsync_WithMultipleMessages_AllAreStored()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var messages = Enumerable.Range(1, 5).Select(i =>
            new AgentMessageRecord(
                Guid.NewGuid(), sessionId, $"agent_{i}", $"Message {i}", i, DateTimeOffset.UtcNow.AddSeconds(i)))
            .ToList();

        // Act
        foreach (var msg in messages)
        {
            await _repository.AddAsync(msg, CancellationToken.None);
        }

        // Assert
        var retrieved = await _repository.GetBySessionIdAsync(sessionId, CancellationToken.None);
        retrieved.Should().HaveCount(5);
    }
}
