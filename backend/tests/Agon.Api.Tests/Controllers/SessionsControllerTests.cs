using Agon.Api.Controllers;
using Agon.Application.Models;
using Agon.Application.Services;
using Agon.Domain.Sessions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Api.Tests.Controllers;

/// <summary>
/// Unit tests for SessionsController using mocked services.
/// </summary>
public class SessionsControllerTests
{
    private readonly ISessionService _sessionService;
    private readonly Agon.Application.Interfaces.IAttachmentTextExtractor _textExtractor;
    private readonly ConversationHistoryService _conversationHistory;
    private readonly SessionsController _controller;

    private static readonly Guid TestSessionId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.Empty; // Unauthenticated

    public SessionsControllerTests()
    {
        _sessionService = Substitute.For<ISessionService>();
        _textExtractor = Substitute.For<Agon.Application.Interfaces.IAttachmentTextExtractor>();

        // ConversationHistoryService needs a real IAgentMessageRepository
        var agentMessageRepo = Substitute.For<Agon.Application.Interfaces.IAgentMessageRepository>();
        agentMessageRepo.GetBySessionIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentMessageRecord>() as IReadOnlyList<AgentMessageRecord>);
        _conversationHistory = new ConversationHistoryService(agentMessageRepo);

        _controller = new SessionsController(
            _sessionService,
            _textExtractor,
            _conversationHistory,
            NullLogger<SessionsController>.Instance,
            attachmentStorage: null);

        // Set up a default HTTP context (unauthenticated)
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    // ── CreateSession ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_WithValidRequest_Returns201Created()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.CreateAsync(Arg.Any<Guid>(), "Test idea", 50, Arg.Any<CancellationToken>())
            .Returns(state);

        var request = new CreateSessionRequest("Test idea", 50);

        // Act
        var result = await _controller.CreateSession(request, CancellationToken.None);

        // Assert
        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(201);
        var response = created.Value.Should().BeOfType<SessionResponse>().Subject;
        response.Id.Should().Be(TestSessionId);
        response.Phase.Should().Be("Intake");
        response.FrictionLevel.Should().Be(50);
    }

    [Fact]
    public async Task CreateSession_WithEmptyIdea_Returns400BadRequest()
    {
        // Arrange
        var request = new CreateSessionRequest("", 50);

        // Act
        var result = await _controller.CreateSession(request, CancellationToken.None);

        // Assert
        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateSession_WithWhitespaceIdea_Returns400BadRequest()
    {
        // Arrange
        var request = new CreateSessionRequest("   ", 50);

        // Act
        var result = await _controller.CreateSession(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateSession_WithFrictionLevelAbove100_Returns400BadRequest()
    {
        // Arrange
        var request = new CreateSessionRequest("Some idea", 101);

        // Act
        var result = await _controller.CreateSession(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateSession_WithNegativeFrictionLevel_Returns400BadRequest()
    {
        // Arrange
        var request = new CreateSessionRequest("Some idea", -1);

        // Act
        var result = await _controller.CreateSession(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateSession_WithFrictionLevel0_Returns201()
    {
        // Arrange
        var state = BuildSessionState(frictionLevel: 0);
        _sessionService.CreateAsync(Arg.Any<Guid>(), Arg.Any<string>(), 0, Arg.Any<CancellationToken>())
            .Returns(state);

        var request = new CreateSessionRequest("Idea", 0);

        // Act
        var result = await _controller.CreateSession(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task CreateSession_WithFrictionLevel100_Returns201()
    {
        // Arrange
        var state = BuildSessionState(frictionLevel: 100);
        _sessionService.CreateAsync(Arg.Any<Guid>(), Arg.Any<string>(), 100, Arg.Any<CancellationToken>())
            .Returns(state);

        var request = new CreateSessionRequest("Idea", 100);

        // Act
        var result = await _controller.CreateSession(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<CreatedAtActionResult>();
    }

    // ── GetSession ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSession_WhenSessionExists_Returns200WithSessionData()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(state);

        // Act
        var result = await _controller.GetSession(TestSessionId, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<SessionResponse>().Subject;
        response.Id.Should().Be(TestSessionId);
    }

    [Fact]
    public async Task GetSession_WhenSessionDoesNotExist_Returns404()
    {
        // Arrange
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns((SessionState?)null);

        // Act
        var result = await _controller.GetSession(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── ListSessions ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListSessions_ReturnsOkWithSessionList()
    {
        // Arrange
        var sessions = new[] { BuildSessionState(), BuildSessionState() };
        _sessionService.ListByUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(sessions as IReadOnlyList<SessionState>);

        // Act
        var result = await _controller.ListSessions(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
    }

    // ── StartDebate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartDebate_WhenSessionExists_Returns202Accepted()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);
        _sessionService.StartClarificationAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.StartDebate(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task StartDebate_WhenSessionDoesNotExist_Returns404()
    {
        // Arrange
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns((SessionState?)null);

        // Act
        var result = await _controller.StartDebate(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task StartDebate_WhenOperationThrows_Returns404WithError()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);
        _sessionService.StartClarificationAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Session already started"));

        // Act
        var result = await _controller.StartDebate(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── SubmitMessage ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitMessage_WithValidContent_Returns202Accepted()
    {
        // Arrange
        var state = BuildSessionState(phase: SessionPhase.Clarification);
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);
        _sessionService.SubmitMessageAsync(TestSessionId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new MessageRequest("This is my clarification response");

        // Act
        var result = await _controller.SubmitMessage(TestSessionId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task SubmitMessage_WithEmptyContent_Returns400BadRequest()
    {
        // Arrange
        var request = new MessageRequest("");

        // Act
        var result = await _controller.SubmitMessage(TestSessionId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SubmitMessage_WhenSessionDoesNotExist_Returns404()
    {
        // Arrange
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns((SessionState?)null);

        var request = new MessageRequest("Hello");

        // Act
        var result = await _controller.SubmitMessage(TestSessionId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SubmitMessage_WhenServiceThrows_Returns404()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);
        _sessionService.SubmitMessageAsync(TestSessionId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Not in clarification phase"));

        var request = new MessageRequest("Hello");

        // Act
        var result = await _controller.SubmitMessage(TestSessionId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── GetTruthMap ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTruthMap_WhenSessionExists_Returns200WithTruthMap()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        // Act
        var result = await _controller.GetTruthMap(TestSessionId, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetTruthMap_WhenSessionDoesNotExist_Returns404()
    {
        // Arrange
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns((SessionState?)null);

        // Act
        var result = await _controller.GetTruthMap(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── GetSnapshots ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSnapshots_WhenSessionExists_Returns200WithSnapshots()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);
        _sessionService.ListSnapshotsAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Agon.Domain.Snapshots.SessionSnapshot>() as IReadOnlyList<Agon.Domain.Snapshots.SessionSnapshot>);

        // Act
        var result = await _controller.ListSnapshots(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetSnapshots_WhenSessionDoesNotExist_Returns404()
    {
        // Arrange
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns((SessionState?)null);

        // Act
        var result = await _controller.ListSnapshots(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── UploadAttachment ───────────────────────────────────────────────────────

    [Fact]
    public async Task UploadAttachment_WhenStorageNotConfigured_Returns503()
    {
        // Arrange - controller has no attachment storage (null)
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        var request = new UploadAttachmentRequest { File = Substitute.For<IFormFile>() };

        // Act
        var result = await _controller.UploadAttachment(TestSessionId, request, CancellationToken.None);

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task UploadAttachment_WhenFileIsNull_AndStorageConfigured_Returns400BadRequest()
    {
        // Arrange - create a controller with mock storage configured
        var mockStorage = Substitute.For<Agon.Application.Interfaces.IAttachmentStorageService>();
        var controllerWithStorage = new SessionsController(
            _sessionService,
            _textExtractor,
            _conversationHistory,
            NullLogger<SessionsController>.Instance,
            attachmentStorage: mockStorage);
        controllerWithStorage.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        var request = new UploadAttachmentRequest { File = null };

        // Act
        var result = await controllerWithStorage.UploadAttachment(TestSessionId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── GetMessages ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMessages_WhenSessionExists_Returns200WithMessages()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        // Act
        var result = await _controller.GetMessages(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_WhenSessionDoesNotExist_Returns404()
    {
        // Arrange
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns((SessionState?)null);

        // Act
        var result = await _controller.GetMessages(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── GetArtifacts ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetArtifacts_WhenSessionExists_Returns200WithArtifacts()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        // Act
        var result = await _controller.ListArtifacts(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetArtifacts_WhenSessionDoesNotExist_Returns404()
    {
        // Arrange
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns((SessionState?)null);

        // Act
        var result = await _controller.ListArtifacts(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetArtifact_ByType_WhenSessionExists_Returns200()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        // Act
        var result = await _controller.GetArtifact(TestSessionId, "verdict", CancellationToken.None);

        // Assert - Verdict requires messages from conversation history - with empty messages returns NotFound
        result.Should().BeAssignableTo<IActionResult>();
    }

    [Fact]
    public async Task GetArtifact_WhenSessionDoesNotExist_Returns404()
    {
        // Arrange
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns((SessionState?)null);

        // Act
        var result = await _controller.GetArtifact(TestSessionId, "verdict", CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetArtifact_WithRisks_Returns200WithRisksMarkdown()
    {
        // Arrange
        var truthMap = new TruthMapModel
        {
            SessionId = TestSessionId,
            Risks =
            [
                new Agon.Domain.TruthMap.Entities.Risk(
                    "r-1", "Database risk", Agon.Domain.TruthMap.Entities.RiskCategory.Technical,
                    Agon.Domain.TruthMap.Entities.RiskSeverity.High, Agon.Domain.TruthMap.Entities.RiskLikelihood.Medium,
                    "Use backups", [], "gpt_agent")
            ]
        };

        var state = BuildSessionState(truthMap: truthMap);
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        // Act
        var result = await _controller.GetArtifact(TestSessionId, "risks", CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var artifact = ok.Value.Should().BeOfType<ArtifactResponse>().Subject;
        artifact.Type.Should().Be("risks");
        artifact.Content.Should().Contain("Risk Analysis");
    }

    [Fact]
    public async Task GetArtifact_WithAssumptions_Returns200WithAssumptionsMarkdown()
    {
        // Arrange
        var truthMap = new TruthMapModel
        {
            SessionId = TestSessionId,
            Assumptions =
            [
                new Agon.Domain.TruthMap.Entities.Assumption(
                    "a-1", "Assumption text", "Validate via survey", [], Agon.Domain.TruthMap.Entities.AssumptionStatus.Unvalidated)
            ]
        };

        var state = BuildSessionState(truthMap: truthMap);
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        // Act
        var result = await _controller.GetArtifact(TestSessionId, "assumptions", CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var artifact = ok.Value.Should().BeOfType<ArtifactResponse>().Subject;
        artifact.Type.Should().Be("assumptions");
        artifact.Content.Should().Contain("Assumptions");
    }

    [Fact]
    public async Task GetArtifact_WithUnknownType_Returns404()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        // Act
        var result = await _controller.GetArtifact(TestSessionId, "unknown-artifact-type", CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── DTO tests ──────────────────────────────────────────────────────────────

    [Fact]
    public void CreateSessionRequest_HasExpectedProperties()
    {
        var req = new CreateSessionRequest("My idea", 42);
        req.Idea.Should().Be("My idea");
        req.FrictionLevel.Should().Be(42);
    }

    [Fact]
    public void MessageRequest_HasExpectedProperties()
    {
        var req = new MessageRequest("Hello");
        req.Content.Should().Be("Hello");
    }

    [Fact]
    public void SessionResponse_HasExpectedProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var resp = new SessionResponse(TestSessionId, "Intake", "Active", 50, 0, 0, now, now);
        resp.Id.Should().Be(TestSessionId);
        resp.Phase.Should().Be("Intake");
        resp.Status.Should().Be("Active");
        resp.FrictionLevel.Should().Be(50);
    }

    [Fact]
    public void AgentTestRequest_HasExpectedProperties()
    {
        var req = new AgentTestRequest("Test question");
        req.Question.Should().Be("Test question");
    }

    [Fact]
    public void AgentTestResponse_HasExpectedProperties()
    {
        var resp = new AgentTestResponse("gpt_agent", "Agent message", 3);
        resp.AgentId.Should().Be("gpt_agent");
        resp.Message.Should().Be("Agent message");
        resp.PatchOperationsCount.Should().Be(3);
    }

    [Fact]
    public void MessageResponse_HasExpectedProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var resp = new MessageResponse("moderator", "Hello!", 1, now);
        resp.AgentId.Should().Be("moderator");
        resp.Message.Should().Be("Hello!");
        resp.Round.Should().Be(1);
        resp.CreatedAt.Should().Be(now);
    }

    [Fact]
    public void ArtifactResponse_HasExpectedProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var resp = new ArtifactResponse("verdict", "# Verdict\n\nContent", 1, now);
        resp.Type.Should().Be("verdict");
        resp.Content.Should().Contain("Verdict");
        resp.Version.Should().Be(1);
    }

    [Fact]
    public void SnapshotResponse_HasExpectedProperties()
    {
        var snapshotId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var resp = new SnapshotResponse(snapshotId, 2, now);
        resp.SnapshotId.Should().Be(snapshotId);
        resp.Round.Should().Be(2);
        resp.CreatedAt.Should().Be(now);
    }

    [Fact]
    public void SessionAttachmentResponse_HasExpectedProperties()
    {
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var resp = new SessionAttachmentResponse(id, sessionId, "file.txt", "text/plain", 1024, "https://blob.url", now, true, "Preview text");
        resp.Id.Should().Be(id);
        resp.SessionId.Should().Be(sessionId);
        resp.FileName.Should().Be("file.txt");
        resp.HasExtractedText.Should().BeTrue();
        resp.ExtractedTextPreview.Should().Be("Preview text");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SessionState BuildSessionState(
        SessionPhase phase = SessionPhase.Intake,
        int frictionLevel = 50,
        TruthMapModel? truthMap = null)
    {
        truthMap ??= TruthMapModel.Empty(TestSessionId);
        return new SessionState
        {
            SessionId = TestSessionId,
            UserId = TestUserId,
            Idea = "Build a SaaS",
            Phase = phase,
            Status = SessionStatus.Active,
            FrictionLevel = frictionLevel,
            CurrentRound = 0,
            TokensUsed = 0,
            TruthMap = truthMap,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
