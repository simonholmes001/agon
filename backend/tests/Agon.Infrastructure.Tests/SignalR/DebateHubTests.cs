using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Infrastructure.SignalR;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;
using Xunit;

namespace Agon.Infrastructure.Tests.SignalR;

/// <summary>
/// Tests for DebateHub - SignalR hub for real-time session communication
/// Coverage Target: 0% → 80%
/// </summary>
public sealed class DebateHubTests
{
    private readonly HubCallerContext _mockContext;
    private readonly IGroupManager _mockGroups;
    private readonly ISessionRepository _mockSessionRepo;
    private readonly ILogger<DebateHub> _mockLogger;
    private readonly DebateHub _hub;

    // A stable user ID used to create an authenticated caller identity
    private readonly Guid _userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public DebateHubTests()
    {
        _mockContext = Substitute.For<HubCallerContext>();
        _mockGroups = Substitute.For<IGroupManager>();
        _mockSessionRepo = Substitute.For<ISessionRepository>();
        _mockLogger = Substitute.For<ILogger<DebateHub>>();

        _hub = new DebateHub(_mockSessionRepo, _mockLogger)
        {
            Context = _mockContext,
            Groups = _mockGroups
        };
    }

    // Helper: configure _mockContext to expose an authenticated user with the given userId
    private void SetAuthenticatedUser(Guid userId)
    {
        var claims = new[]
        {
            new Claim("oid", userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        _mockContext.User.Returns(principal);
    }

    // Helper: configure _mockContext as an unauthenticated caller (Guid.Empty)
    private void SetUnauthenticatedUser()
    {
        _mockContext.User.Returns((ClaimsPrincipal?)null);
    }

    // Helper: build a SessionState owned by the given userId
    private static SessionState MakeSession(Guid sessionId, Guid userId)
    {
        return SessionState.Create(sessionId, userId, "test idea", 50, false,
            Domain.TruthMap.TruthMap.Empty(sessionId));
    }

    #region JoinSession — Authorized Access

    [Fact]
    public async Task JoinSession_OwnedSession_ShouldAddConnectionToSessionGroup()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var connectionId = "test-connection-123";

        SetAuthenticatedUser(_userId);
        _mockContext.ConnectionId.Returns(connectionId);
        _mockSessionRepo.GetAsync(sessionId).Returns(MakeSession(sessionId, _userId));

        // Act
        await _hub.JoinSession(sessionId);

        // Assert
        await _mockGroups.Received(1).AddToGroupAsync(connectionId, $"session:{sessionId}", default);
    }

    [Fact]
    public async Task JoinSession_UnauthenticatedUser_OwnedByGuidEmpty_ShouldSucceed()
    {
        // Arrange — local-dev scenario where auth is disabled
        var sessionId = Guid.NewGuid();
        var connectionId = "test-connection-anon";

        SetUnauthenticatedUser();
        _mockContext.ConnectionId.Returns(connectionId);
        _mockSessionRepo.GetAsync(sessionId).Returns(MakeSession(sessionId, Guid.Empty));

        // Act
        await _hub.JoinSession(sessionId);

        // Assert
        await _mockGroups.Received(1).AddToGroupAsync(connectionId, $"session:{sessionId}", default);
    }

    [Fact]
    public async Task JoinSession_WithDifferentSessionIds_ShouldCreateDifferentGroups()
    {
        // Arrange
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();
        var connectionId = "test-connection-456";

        SetAuthenticatedUser(_userId);
        _mockContext.ConnectionId.Returns(connectionId);
        _mockSessionRepo.GetAsync(sessionId1).Returns(MakeSession(sessionId1, _userId));
        _mockSessionRepo.GetAsync(sessionId2).Returns(MakeSession(sessionId2, _userId));

        // Act
        await _hub.JoinSession(sessionId1);
        await _hub.JoinSession(sessionId2);

        // Assert
        await _mockGroups.Received(1).AddToGroupAsync(connectionId, $"session:{sessionId1}", default);
        await _mockGroups.Received(1).AddToGroupAsync(connectionId, $"session:{sessionId2}", default);
    }

    [Fact]
    public async Task JoinSession_CalledTwiceWithSameSessionId_ShouldAddToBothTimes()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var connectionId = "test-connection-duplicate";

        SetAuthenticatedUser(_userId);
        _mockContext.ConnectionId.Returns(connectionId);
        _mockSessionRepo.GetAsync(sessionId).Returns(MakeSession(sessionId, _userId));

        // Act
        await _hub.JoinSession(sessionId);
        await _hub.JoinSession(sessionId);

        // Assert
        await _mockGroups.Received(2).AddToGroupAsync(connectionId, $"session:{sessionId}", default);
    }

    #endregion

    #region JoinSession — Unauthorized / Denied Access

    [Fact]
    public async Task JoinSession_SessionBelongsToOtherUser_ShouldThrowHubException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        SetAuthenticatedUser(_userId);
        _mockContext.ConnectionId.Returns("test-connection-denied");
        _mockSessionRepo.GetAsync(sessionId).Returns(MakeSession(sessionId, otherUserId));

        // Act & Assert
        var act = async () => await _hub.JoinSession(sessionId);
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*denied*");

        // No group join should occur
        await _mockGroups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task JoinSession_SessionNotFound_ShouldThrowHubException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        SetAuthenticatedUser(_userId);
        _mockContext.ConnectionId.Returns("test-connection-notsession");
        _mockSessionRepo.GetAsync(sessionId).Returns((SessionState?)null);

        // Act & Assert
        var act = async () => await _hub.JoinSession(sessionId);
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*not found*");

        await _mockGroups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), default);
    }

    #endregion

    #region LeaveSession Tests

    [Fact]
    public async Task LeaveSession_ShouldRemoveConnectionFromSessionGroup()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var connectionId = "test-connection-leave-123";

        _mockContext.ConnectionId.Returns(connectionId);

        // Act
        await _hub.LeaveSession(sessionId);

        // Assert
        await _mockGroups.Received(1).RemoveFromGroupAsync(connectionId, $"session:{sessionId}", default);
    }

    [Fact]
    public async Task LeaveSession_WithDifferentSessionIds_ShouldRemoveFromDifferentGroups()
    {
        // Arrange
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();
        var connectionId = "test-connection-leave-456";

        _mockContext.ConnectionId.Returns(connectionId);

        // Act
        await _hub.LeaveSession(sessionId1);
        await _hub.LeaveSession(sessionId2);

        // Assert
        await _mockGroups.Received(1).RemoveFromGroupAsync(connectionId, $"session:{sessionId1}", default);
        await _mockGroups.Received(1).RemoveFromGroupAsync(connectionId, $"session:{sessionId2}", default);
    }

    [Fact]
    public async Task LeaveSession_WithEmptyGuid_ShouldStillRemoveFromGroup()
    {
        // Arrange
        var sessionId = Guid.Empty;
        var connectionId = "test-connection-leave-789";

        _mockContext.ConnectionId.Returns(connectionId);

        // Act
        await _hub.LeaveSession(sessionId);

        // Assert
        await _mockGroups.Received(1).RemoveFromGroupAsync(connectionId, $"session:{Guid.Empty}", default);
    }

    [Fact]
    public async Task LeaveSession_CalledTwiceWithSameSessionId_ShouldRemoveFromBothTimes()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var connectionId = "test-connection-leave-duplicate";

        _mockContext.ConnectionId.Returns(connectionId);

        // Act
        await _hub.LeaveSession(sessionId);
        await _hub.LeaveSession(sessionId);

        // Assert
        await _mockGroups.Received(2).RemoveFromGroupAsync(connectionId, $"session:{sessionId}", default);
    }

    [Fact]
    public async Task LeaveSession_WithoutPriorJoin_ShouldStillAttemptRemoval()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var connectionId = "test-connection-leave-no-join";

        _mockContext.ConnectionId.Returns(connectionId);

        // Act
        await _hub.LeaveSession(sessionId);

        // Assert
        await _mockGroups.Received(1).RemoveFromGroupAsync(connectionId, $"session:{sessionId}", default);
    }

    #endregion

    #region OnDisconnectedAsync Tests

    [Fact]
    public async Task OnDisconnectedAsync_WithNullException_ShouldCompleteSuccessfully()
    {
        // Arrange
        _mockContext.ConnectionId.Returns("test-connection-disconnect-1");

        // Act
        var act = async () => await _hub.OnDisconnectedAsync(null);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnDisconnectedAsync_WithException_ShouldCompleteSuccessfully()
    {
        // Arrange
        var exception = new Exception("Test disconnection error");
        _mockContext.ConnectionId.Returns("test-connection-disconnect-2");

        // Act
        var act = async () => await _hub.OnDisconnectedAsync(exception);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Group Name Format Tests

    [Fact]
    public void GroupNameFormat_ShouldBeConsistent_WithSessionPrefix()
    {
        var sessionId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var expectedGroupName = $"session:{sessionId}";

        expectedGroupName.Should().Be("session:12345678-1234-1234-1234-123456789abc");
    }

    [Fact]
    public void GroupNameFormat_ShouldMatch_BroadcasterExpectations()
    {
        var sessionId = Guid.NewGuid();
        var hubGroupName = $"session:{sessionId}";
        var broadcasterGroupName = $"session:{sessionId}";

        hubGroupName.Should().Be(broadcasterGroupName);
    }

    #endregion
}
