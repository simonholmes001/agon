using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Services;
using FluentAssertions;
using NSubstitute;

namespace Agon.Application.Tests.Services;

public class ConversationHistoryServiceTests
{
    private readonly IAgentMessageRepository _mockMessageRepo;
    private readonly ConversationHistoryService _service;

    public ConversationHistoryServiceTests()
    {
        _mockMessageRepo = Substitute.For<IAgentMessageRepository>();
        _service = new ConversationHistoryService(_mockMessageRepo);
    }

    [Fact]
    public async Task StoreMessageAsync_WithValidMessage_StoresInRepository()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var agentId = "moderator";
        var message = "What is your target user persona?";
        var round = 1;

        // Act
        await _service.StoreMessageAsync(sessionId, agentId, message, round, CancellationToken.None);

        // Assert
        await _mockMessageRepo.Received(1).AddAsync(
            Arg.Is<AgentMessageRecord>(m => 
                m.SessionId == sessionId &&
                m.AgentId == agentId &&
                m.Message == message &&
                m.Round == round),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsMessagesInChronologicalOrder()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var messages = new List<AgentMessageRecord>
        {
            new(Guid.NewGuid(), sessionId, "moderator", "Question 1", 1, DateTimeOffset.UtcNow.AddMinutes(-5)),
            new(Guid.NewGuid(), sessionId, "user", "Answer 1", 1, DateTimeOffset.UtcNow.AddMinutes(-4)),
            new(Guid.NewGuid(), sessionId, "moderator", "Question 2", 2, DateTimeOffset.UtcNow.AddMinutes(-3))
        };

        _mockMessageRepo.GetBySessionIdAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(messages);

        // Act
        var result = await _service.GetMessagesAsync(sessionId, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result[0].Message.Should().Be("Question 1");
        result[1].Message.Should().Be("Answer 1");
        result[2].Message.Should().Be("Question 2");
    }

    [Fact]
    public async Task GetMessagesAsync_WithNonExistentSession_ReturnsEmptyList()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _mockMessageRepo.GetBySessionIdAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(new List<AgentMessageRecord>());

        // Act
        var result = await _service.GetMessagesAsync(sessionId, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreMessageAsync_WithEmptyMessage_ThrowsArgumentException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.StoreMessageAsync(sessionId, "moderator", "", 1, CancellationToken.None));
    }

    [Fact]
    public async Task StoreMessageAsync_WithNullAgentId_ThrowsArgumentException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.StoreMessageAsync(sessionId, null!, "message", 1, CancellationToken.None));
    }
}
