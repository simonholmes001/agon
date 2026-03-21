using Agon.Api.Controllers;
using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Services;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Api.Tests.Controllers;

/// <summary>
/// Additional tests for SessionsController covering artifact generation from
/// TruthMap entities (risks, assumptions) and UploadAttachment success path.
/// </summary>
public class SessionsControllerArtifactTests
{
    private readonly ISessionService _sessionService;
    private readonly IAttachmentTextExtractor _textExtractor;
    private readonly ConversationHistoryService _conversationHistory;
    private readonly IAgentMessageRepository _agentMessageRepo;
    private readonly SessionsController _controller;

    private static readonly Guid TestSessionId = Guid.NewGuid();

    public SessionsControllerArtifactTests()
    {
        _sessionService = Substitute.For<ISessionService>();
        _textExtractor = Substitute.For<IAttachmentTextExtractor>();

        _agentMessageRepo = Substitute.For<IAgentMessageRepository>();
        _agentMessageRepo.GetBySessionIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentMessageRecord>() as IReadOnlyList<AgentMessageRecord>);
        _conversationHistory = new ConversationHistoryService(_agentMessageRepo);

        _controller = new SessionsController(
            _sessionService,
            _textExtractor,
            _conversationHistory,
            NullLogger<SessionsController>.Instance,
            attachmentStorage: null);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    // ── GetArtifact "risks" type ───────────────────────────────────────────────

    [Fact]
    public async Task GetArtifact_Risks_WhenRisksExistInTruthMap_ReturnsRisksContent()
    {
        // Arrange
        var truthMap = TruthMapModel.Empty(TestSessionId) with
        {
            Risks =
            [
                new Risk("r-1", "Competition from established players", RiskCategory.Market, RiskSeverity.High, RiskLikelihood.High, "Differentiate via niche focus", [], "gemini_agent"),
                new Risk("r-2", "Scaling infrastructure challenges", RiskCategory.Technical, RiskSeverity.Medium, RiskLikelihood.Medium, "Use managed cloud services", [], "gpt_agent")
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
        artifact.Content.Should().Contain("Competition from established players");
        artifact.Content.Should().Contain("Scaling infrastructure challenges");
        artifact.Content.Should().Contain("High");
    }

    [Fact]
    public async Task GetArtifact_Risks_WhenNoRisks_FallsBackToSynthesizerMessage()
    {
        // Arrange - empty risks in truth map
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        var messages = new[]
        {
            new AgentMessageRecord(Guid.NewGuid(), TestSessionId, "synthesizer", "Synthesized risk analysis", 2, DateTimeOffset.UtcNow)
        } as IReadOnlyList<AgentMessageRecord>;

        _agentMessageRepo.GetBySessionIdAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(messages);

        // Act
        var result = await _controller.GetArtifact(TestSessionId, "risks", CancellationToken.None);

        // Assert - falls back to synthesizer message
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var artifact = ok.Value.Should().BeOfType<ArtifactResponse>().Subject;
        artifact.Content.Should().Contain("Synthesized risk analysis");
    }

    // ── GetArtifact "assumptions" type ────────────────────────────────────────

    [Fact]
    public async Task GetArtifact_Assumptions_WhenAssumptionsExist_ReturnsAssumptionsContent()
    {
        // Arrange
        var truthMap = TruthMapModel.Empty(TestSessionId) with
        {
            Assumptions =
            [
                new Assumption("a-1", "Target users will pay $50/month", "Survey 50 potential users", [], AssumptionStatus.Unvalidated),
                new Assumption("a-2", "Integration with Jira is required", "Conduct 5 user interviews", [], AssumptionStatus.Validated)
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
        artifact.Content.Should().Contain("Target users will pay $50/month");
        artifact.Content.Should().Contain("Jira");
    }

    [Fact]
    public async Task GetArtifact_Assumptions_WhenNoAssumptions_ReturnsNotFound()
    {
        // Arrange - empty assumptions, no agent messages
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);
        _agentMessageRepo.GetBySessionIdAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentMessageRecord>() as IReadOnlyList<AgentMessageRecord>);

        // Act
        var result = await _controller.GetArtifact(TestSessionId, "assumptions", CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── GetArtifact session not found ─────────────────────────────────────────

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

    // ── UploadAttachment success path ─────────────────────────────────────────

    [Fact]
    public async Task UploadAttachment_WhenStorageConfiguredAndFileValid_Returns201Created()
    {
        // Arrange
        var mockStorage = Substitute.For<IAttachmentStorageService>();
        var uploadResult = new AttachmentUploadResult(
            BlobName: "blobs/test.pdf",
            BlobUri: "https://storage.azure.com/blobs/test.pdf",
            AccessUrl: "https://storage.azure.com/blobs/test.pdf?sas=token");

        mockStorage.UploadAsync(
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(uploadResult);

        _textExtractor.ExtractAsync(
            Arg.Any<byte[]>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns("Extracted text from PDF");

        var savedAttachment = new SessionAttachment(
            AttachmentId: Guid.NewGuid(),
            SessionId: TestSessionId,
            UserId: Guid.Empty,
            FileName: "test.pdf",
            ContentType: "application/pdf",
            SizeBytes: 1024,
            BlobName: "blobs/test.pdf",
            BlobUri: "https://storage.azure.com/blobs/test.pdf",
            AccessUrl: "https://storage.azure.com/blobs/test.pdf?sas=token",
            ExtractedText: "Extracted text from PDF",
            UploadedAt: DateTimeOffset.UtcNow);

        _sessionService.SaveAttachmentAsync(
            Arg.Any<SessionAttachment>(),
            Arg.Any<CancellationToken>())
            .Returns(savedAttachment);

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

        // Set up session
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        // Create a mock IFormFile with 1KB content
        var fileContent = new byte[1024];
        var mockFile = Substitute.For<IFormFile>();
        mockFile.Length.Returns(fileContent.Length);
        mockFile.FileName.Returns("test.pdf");
        mockFile.ContentType.Returns("application/pdf");
        mockFile.OpenReadStream().Returns(new MemoryStream(fileContent));

        var request = new UploadAttachmentRequest { File = mockFile };

        // Act
        var result = await controllerWithStorage.UploadAttachment(TestSessionId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task UploadAttachment_WhenSaveAttachmentThrowsInvalidOperation_Returns503()
    {
        // Arrange
        var mockStorage = Substitute.For<IAttachmentStorageService>();
        var uploadResult = new AttachmentUploadResult("blob", "uri", "access");
        mockStorage.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(uploadResult);

        _textExtractor.ExtractAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        // SaveAttachmentAsync throws when persistence isn't configured
        _sessionService.SaveAttachmentAsync(Arg.Any<SessionAttachment>(), Arg.Any<CancellationToken>())
            .Returns<SessionAttachment>(_ => throw new InvalidOperationException("Persistence not configured"));

        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

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

        var fileContent = new byte[512];
        var mockFile = Substitute.For<IFormFile>();
        mockFile.Length.Returns(fileContent.Length);
        mockFile.FileName.Returns("doc.txt");
        mockFile.ContentType.Returns("text/plain");
        mockFile.OpenReadStream().Returns(new MemoryStream(fileContent));

        var request = new UploadAttachmentRequest { File = mockFile };

        // Act
        var result = await controllerWithStorage.UploadAttachment(TestSessionId, request, CancellationToken.None);

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(503);
    }

    // ── ListArtifacts session ownership ──────────────────────────────────────

    [Fact]
    public async Task ListArtifacts_WhenSessionExistsWithNoMessages_ReturnsEmptyOrOnlyTruthMapArtifacts()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        // Act
        var result = await _controller.ListArtifacts(TestSessionId, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        // No synthesizer messages and no risks/assumptions → empty list
        var artifacts = ok.Value.Should().BeAssignableTo<List<ArtifactResponse>>().Subject;
        artifacts.Should().BeEmpty();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SessionState BuildSessionState(
        SessionPhase phase = SessionPhase.Intake,
        TruthMapModel? truthMap = null)
    {
        truthMap ??= TruthMapModel.Empty(TestSessionId);
        return new SessionState
        {
            SessionId = TestSessionId,
            UserId = Guid.Empty,
            Idea = "Build a SaaS",
            Phase = phase,
            Status = SessionStatus.Active,
            FrictionLevel = 50,
            CurrentRound = 0,
            TokensUsed = 0,
            TruthMap = truthMap,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
