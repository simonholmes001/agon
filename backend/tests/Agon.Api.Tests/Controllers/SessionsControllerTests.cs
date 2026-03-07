using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Services;
using Agon.Domain.Sessions;
using Agon.Domain.Snapshots;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;

namespace Agon.Api.Tests.Controllers;

/// <summary>
/// Integration tests for SessionsController using WebApplicationFactory.
/// Tests verify HTTP endpoints map correctly to Application services.
/// </summary>
public class SessionsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ISessionService _mockSessionService;
    private readonly HttpClient _client;

    public SessionsControllerTests(WebApplicationFactory<Program> factory)
    {
        _mockSessionService = Substitute.For<ISessionService>();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            // Set environment to "Testing" to skip database/orchestrator registration
            builder.UseEnvironment("Testing");
            
            builder.ConfigureServices(services =>
            {
                // Replace real SessionService with mock
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(ISessionService));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddScoped(_ => _mockSessionService);
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task POST_Sessions_CreatesNewSession_ReturnsCreated()
    {
        // Arrange
        var request = new
        {
            idea = "Build an AI-powered strategy analysis tool",
            frictionLevel = 50
        };

        var expectedSessionId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedSessionState = SessionState.Create(
            expectedSessionId,
            expectedUserId,
            request.idea,
            request.frictionLevel,
            researchToolsEnabled: false,
            Domain.TruthMap.TruthMap.Empty(expectedSessionId));

        _mockSessionService.CreateAsync(
            Arg.Any<Guid>(),
            Arg.Is<string>(idea => idea == request.idea),
            Arg.Is<int>(friction => friction == request.frictionLevel),
            Arg.Any<CancellationToken>())
            .Returns(expectedSessionState);

        // Act
        var response = await _client.PostAsJsonAsync("/sessions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().ContainEquivalentOf($"sessions/{expectedSessionId}");

        var result = await response.Content.ReadFromJsonAsync<SessionResponse>();
        result.Should().NotBeNull();
        result!.SessionId.Should().Be(expectedSessionId);
        result.Phase.Should().Be("Intake");
        result.Status.Should().Be("Active");

        await _mockSessionService.Received(1).CreateAsync(
            Arg.Any<Guid>(),
            request.idea,
            request.frictionLevel,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GET_SessionById_ReturnsSessionState()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionState = SessionState.Create(
            sessionId,
            userId,
            "Test idea",
            frictionLevel: 70,
            researchToolsEnabled: false,
            Domain.TruthMap.TruthMap.Empty(sessionId));
        
        // Simulate state after one clarification round
        sessionState.Phase = SessionPhase.Clarification;
        sessionState.CurrentRound = 1;
        sessionState.TokensUsed = 1500;

        _mockSessionService.GetAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(sessionState);

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SessionResponse>();
        result.Should().NotBeNull();
        result!.SessionId.Should().Be(sessionId);
        result.Phase.Should().Be("Clarification");
        result.FrictionLevel.Should().Be(70);
        result.RoundCount.Should().Be(1);
        result.TokensUsed.Should().Be(1500);
    }

    [Fact]
    public async Task GET_SessionById_WhenNotFound_ReturnsNotFound()
    {
        // Arrange
        var nonExistentSessionId = Guid.NewGuid();
        _mockSessionService.GetAsync(nonExistentSessionId, Arg.Any<CancellationToken>())
            .Returns((SessionState?)null);

        // Act
        var response = await _client.GetAsync($"/sessions/{nonExistentSessionId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_SessionsStart_StartsDebate_ReturnsAccepted()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _mockSessionService.StartClarificationAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var response = await _client.PostAsync($"/sessions/{sessionId}/start", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await _mockSessionService.Received(1).StartClarificationAsync(
            sessionId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task POST_SessionMessages_SubmitsUserMessage_ReturnsAccepted()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var messageRequest = new { content = "Yes, target users are enterprise teams" };

        _mockSessionService.GetAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(SessionState.Create(
                sessionId,
                Guid.NewGuid(),
                "Test idea",
                50,
                false,
                Domain.TruthMap.TruthMap.Empty(sessionId)));

        // Act
        var response = await _client.PostAsJsonAsync($"/sessions/{sessionId}/messages", messageRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task GET_SessionTruthMap_ReturnsTruthMapState()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var truthMap = Domain.TruthMap.TruthMap.Empty(sessionId);
        var sessionState = SessionState.Create(
            sessionId,
            Guid.NewGuid(),
            "Test idea",
            50,
            false,
            truthMap);

        _mockSessionService.GetAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(sessionState);

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/truthmap");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsStringAsync();
        result.Should().Contain("sessionId");
    }

    [Fact]
    public async Task GET_SessionSnapshots_ReturnsSnapshotList()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var snapshots = new List<SessionSnapshot>
        {
            SessionSnapshot.Create(Domain.TruthMap.TruthMap.Empty(sessionId), round: 1)
        };

        _mockSessionService.ListSnapshotsAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SessionSnapshot>>(snapshots));

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/snapshots");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

// DTOs for API responses
public record SessionResponse(
    Guid SessionId,
    string Phase,
    string Status,
    int FrictionLevel,
    int RoundCount,
    int TokensUsed);
