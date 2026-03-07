using Agon.Application.Interfaces;
using Agon.Domain.TruthMap.Entities;
using Agon.Infrastructure.SignalR;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Agon.Infrastructure.Tests.SignalR;

/// <summary>
/// Unit tests for SignalR Event Broadcaster using mocked IHubContext.
/// Tests verify that SignalR methods are called with correct groups and payloads.
/// </summary>
public class SignalREventBroadcasterTests
{
    private readonly IHubContext<DebateHub> _mockHubContext;
    private readonly IClientProxy _mockClientProxy;
    private readonly IHubClients _mockClients;
    private readonly IEventBroadcaster _broadcaster;

    public SignalREventBroadcasterTests()
    {
        _mockClientProxy = Substitute.For<IClientProxy>();
        _mockClients = Substitute.For<IHubClients>();
        _mockHubContext = Substitute.For<IHubContext<DebateHub>>();

        _mockHubContext.Clients.Returns(_mockClients);
        _mockClients.Group(Arg.Any<string>()).Returns(_mockClientProxy);

        _broadcaster = new SignalREventBroadcaster(_mockHubContext);
    }

    [Fact]
    public async Task SendTokenAsync_SendsToSessionGroup()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var agentId = "MOD";
        var token = "Hello ";
        var isComplete = false;

        // Act
        await _broadcaster.SendTokenAsync(sessionId, agentId, token, isComplete);

        // Assert
        _mockClients.Received(1).Group($"session:{sessionId}");
        await _mockClientProxy.Received(1).SendCoreAsync(
            "AgentToken",
            Arg.Is<object[]>(args =>
                args.Length == 3 &&
                args[0].ToString() == agentId &&
                args[1].ToString() == token &&
                (bool)args[2] == isComplete),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendRoundProgressAsync_BroadcastsPhaseTransition()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var phase = "ANALYSIS_ROUND";
        var status = "Active";

        // Act
        await _broadcaster.SendRoundProgressAsync(sessionId, phase, status);

        // Assert
        _mockClients.Received(1).Group($"session:{sessionId}");
        await _mockClientProxy.Received(1).SendCoreAsync(
            "RoundProgress",
            Arg.Is<object[]>(args =>
                args.Length == 2 &&
                args[0].ToString() == phase &&
                args[1].ToString() == status),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendTruthMapPatchAsync_BroadcastsPatchEvent()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var patch = new { operation = "add", path = "/claims/c1", value = "new claim" };
        var version = 5;

        // Act
        await _broadcaster.SendTruthMapPatchAsync(sessionId, patch, version);

        // Assert
        _mockClients.Received(1).Group($"session:{sessionId}");
        await _mockClientProxy.Received(1).SendCoreAsync(
            "TruthMapPatch",
            Arg.Is<object[]>(args =>
                args.Length == 2 &&
                args[0] == patch &&
                (int)args[1] == version),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendConfidenceTransitionAsync_BroadcastsTransition()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var transition = new ConfidenceTransition(
            ClaimId: "c1",
            FromConfidence: 0.75f,
            ToConfidence: 0.45f,
            Reason: ConfidenceTransitionReason.ChallengedNoDefense,
            Round: 3,
            OccurredAt: DateTimeOffset.UtcNow);

        // Act
        await _broadcaster.SendConfidenceTransitionAsync(sessionId, transition);

        // Assert
        _mockClients.Received(1).Group($"session:{sessionId}");
        await _mockClientProxy.Received(1).SendCoreAsync(
            "ConfidenceTransition",
            Arg.Is<object[]>(args =>
                args.Length == 1 &&
                args[0] == transition),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendConvergenceUpdateAsync_BroadcastsConvergenceScores()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var convergence = new { claimStability = 0.85, frameworkSimilarity = 0.72 };

        // Act
        await _broadcaster.SendConvergenceUpdateAsync(sessionId, convergence);

        // Assert
        _mockClients.Received(1).Group($"session:{sessionId}");
        await _mockClientProxy.Received(1).SendCoreAsync(
            "ConvergenceUpdate",
            Arg.Is<object[]>(args =>
                args.Length == 1 &&
                args[0] == convergence),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendPendingRevalidationAsync_BroadcastsEntityIds()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var entityIds = new List<string> { "c1", "c2", "e1" };

        // Act
        await _broadcaster.SendPendingRevalidationAsync(sessionId, entityIds);

        // Assert
        _mockClients.Received(1).Group($"session:{sessionId}");
        await _mockClientProxy.Received(1).SendCoreAsync(
            "PendingRevalidation",
            Arg.Is<object[]>(args =>
                args.Length == 1 &&
                args[0] == entityIds),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendBudgetWarningAsync_BroadcastsWarning()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var percentUsed = 85.5f;
        var message = "Budget usage at 85%";

        // Act
        await _broadcaster.SendBudgetWarningAsync(sessionId, percentUsed, message);

        // Assert
        _mockClients.Received(1).Group($"session:{sessionId}");
        await _mockClientProxy.Received(1).SendCoreAsync(
            "BudgetWarning",
            Arg.Is<object[]>(args =>
                args.Length == 2 &&
                Math.Abs((float)args[0] - percentUsed) < 0.01f &&
                args[1].ToString() == message),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendArtifactReadyAsync_BroadcastsArtifactNotification()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var artifactType = "ExecutiveSummary";
        var version = 3;

        // Act
        await _broadcaster.SendArtifactReadyAsync(sessionId, artifactType, version);

        // Assert
        _mockClients.Received(1).Group($"session:{sessionId}");
        await _mockClientProxy.Received(1).SendCoreAsync(
            "ArtifactReady",
            Arg.Is<object[]>(args =>
                args.Length == 2 &&
                args[0].ToString() == artifactType &&
                (int)args[1] == version),
            Arg.Any<CancellationToken>());
    }
}
