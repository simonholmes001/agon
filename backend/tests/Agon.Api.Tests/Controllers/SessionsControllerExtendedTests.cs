using Agon.Api.Controllers;
using Agon.Application.Interfaces;
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
/// Extended unit tests for SessionsController covering HITL endpoints,
/// attachment management, expanded artifact scenarios, and TestAgent.
/// </summary>
public class SessionsControllerExtendedTests
{
    private readonly ISessionService _sessionService;
    private readonly IAttachmentTextExtractor _textExtractor;
    private readonly ConversationHistoryService _conversationHistory;
    private readonly IAgentMessageRepository _agentMessageRepo;
    private readonly SessionsController _controller;

    private static readonly Guid TestSessionId = Guid.NewGuid();

    public SessionsControllerExtendedTests()
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

    // ── TestAgent ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TestAgent_WhenNoAgentsConfigured_Returns500()
    {
        // Arrange
        var request = new AgentTestRequest("How does this work?");
        var emptyAgents = Array.Empty<ICouncilAgent>() as IReadOnlyList<ICouncilAgent>;

        // Act
        var result = await _controller.TestAgent(request, emptyAgents, CancellationToken.None);

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task TestAgent_WhenAgentResponds_Returns200WithResponse()
    {
        // Arrange
        var request = new AgentTestRequest("Test question about viability");

        var mockAgent = Substitute.For<ICouncilAgent>();
        mockAgent.AgentId.Returns("gpt_agent");
        mockAgent.RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>())
            .Returns(new AgentResponse("gpt_agent", "This is the agent's analysis", null, 100, false, null));

        var agents = new[] { mockAgent } as IReadOnlyList<ICouncilAgent>;

        // Act
        var result = await _controller.TestAgent(request, agents, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<AgentTestResponse>().Subject;
        response.AgentId.Should().Be("gpt_agent");
        response.Message.Should().Be("This is the agent's analysis");
    }

    [Fact]
    public async Task TestAgent_WhenAgentThrows_Returns500WithError()
    {
        // Arrange
        var request = new AgentTestRequest("Test question");

        var mockAgent = Substitute.For<ICouncilAgent>();
        mockAgent.AgentId.Returns("gpt_agent");
        mockAgent.RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>())
            .Throws(new Exception("Provider API error"));

        var agents = new[] { mockAgent } as IReadOnlyList<ICouncilAgent>;

        // Act
        var result = await _controller.TestAgent(request, agents, CancellationToken.None);

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task TestAgent_WhenAgentReturnsWithPatch_ReturnsPatchOperationsCount1()
    {
        // Arrange
        var request = new AgentTestRequest("Test question");
        var patch = new Agon.Domain.TruthMap.TruthMapPatch(
            Ops: [new Agon.Domain.TruthMap.PatchOperation(Agon.Domain.TruthMap.PatchOp.Add, "/claims/-", null)],
            Meta: new Agon.Domain.TruthMap.PatchMeta("gpt_agent", 1, "test", Guid.Empty)
        );

        var mockAgent = Substitute.For<ICouncilAgent>();
        mockAgent.AgentId.Returns("gpt_agent");
        mockAgent.RunAsync(Arg.Any<AgentContext>(), Arg.Any<CancellationToken>())
            .Returns(new AgentResponse("gpt_agent", "Analysis", patch, 50, false, null));

        var agents = new[] { mockAgent } as IReadOnlyList<ICouncilAgent>;

        // Act
        var result = await _controller.TestAgent(request, agents, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<AgentTestResponse>().Subject;
        response.PatchOperationsCount.Should().Be(1);
    }

    // ── ListAttachments ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAttachments_WhenSessionExists_Returns200WithAttachments()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);
        _sessionService.ListAttachmentsAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SessionAttachment>() as IReadOnlyList<SessionAttachment>);

        // Act
        var result = await _controller.ListAttachments(TestSessionId, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ListAttachments_WhenSessionDoesNotExist_Returns404()
    {
        // Arrange
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns((SessionState?)null);

        // Act
        var result = await _controller.ListAttachments(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ListAttachments_WhenSessionHasAttachments_ReturnsAllAttachments()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        var attachments = new[]
        {
            new SessionAttachment(Guid.NewGuid(), TestSessionId, Guid.Empty, "file1.pdf", "application/pdf", 1024, "blob1", "https://uri1", "https://access1", null, DateTimeOffset.UtcNow),
            new SessionAttachment(Guid.NewGuid(), TestSessionId, Guid.Empty, "file2.txt", "text/plain", 512, "blob2", "https://uri2", "https://access2", "Extracted text", DateTimeOffset.UtcNow)
        } as IReadOnlyList<SessionAttachment>;

        _sessionService.ListAttachmentsAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(attachments);

        // Act
        var result = await _controller.ListAttachments(TestSessionId, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value.Should().BeAssignableTo<System.Collections.IEnumerable>().Subject;
        list.Cast<object>().Should().HaveCount(2);
    }

    // ── GetArtifact with synthesizer messages ──────────────────────────────────

    [Fact]
    public async Task GetArtifact_WhenSynthesizerMessageExists_ReturnsArtifactWithContent()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        var messages = new[]
        {
            new AgentMessageRecord(Guid.NewGuid(), TestSessionId, "synthesizer", "## Verdict\nThe idea is viable.", 2, DateTimeOffset.UtcNow),
            new AgentMessageRecord(Guid.NewGuid(), TestSessionId, "gpt_agent", "Analysis content", 1, DateTimeOffset.UtcNow.AddMinutes(-1))
        } as IReadOnlyList<AgentMessageRecord>;

        _agentMessageRepo.GetBySessionIdAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(messages);

        // Act
        var result = await _controller.GetArtifact(TestSessionId, "verdict", CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var artifact = ok.Value.Should().BeOfType<ArtifactResponse>().Subject;
        artifact.Content.Should().Contain("Verdict");
    }

    [Fact]
    public async Task GetArtifact_WhenNoSynthesizerMessageButOtherAgentExists_ReturnsFallbackContent()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        var messages = new[]
        {
            new AgentMessageRecord(Guid.NewGuid(), TestSessionId, "gpt_agent", "GPT analysis", 1, DateTimeOffset.UtcNow)
        } as IReadOnlyList<AgentMessageRecord>;

        _agentMessageRepo.GetBySessionIdAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(messages);

        // Act - verdict requires synthesizer or fallback
        var result = await _controller.GetArtifact(TestSessionId, "verdict", CancellationToken.None);

        // Assert - falls back to gpt_agent message
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetArtifact_Plan_ReturnsFromSynthesizerMessage()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        var messages = new[]
        {
            new AgentMessageRecord(Guid.NewGuid(), TestSessionId, "synthesizer", "## 30/60/90 Day Plan\n...", 2, DateTimeOffset.UtcNow)
        } as IReadOnlyList<AgentMessageRecord>;

        _agentMessageRepo.GetBySessionIdAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(messages);

        // Act
        var result = await _controller.GetArtifact(TestSessionId, "plan", CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var artifact = ok.Value.Should().BeOfType<ArtifactResponse>().Subject;
        artifact.Type.Should().Be("plan");
    }

    // ── ListArtifacts with synthesizer messages ──────────────────────────────────

    [Fact]
    public async Task ListArtifacts_WhenSynthesizerMessageExists_IncludesVerdictAndPlan()
    {
        // Arrange
        var state = BuildSessionState();
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        var messages = new[]
        {
            new AgentMessageRecord(Guid.NewGuid(), TestSessionId, "synthesizer", "Final synthesis", 2, DateTimeOffset.UtcNow)
        } as IReadOnlyList<AgentMessageRecord>;

        _agentMessageRepo.GetBySessionIdAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns(messages);

        // Act
        var result = await _controller.ListArtifacts(TestSessionId, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var artifacts = ok.Value.Should().BeAssignableTo<List<ArtifactResponse>>().Subject;
        artifacts.Should().Contain(a => a.Type == "verdict");
        artifacts.Should().Contain(a => a.Type == "plan");
    }

    [Fact]
    public async Task ListArtifacts_WithRisksAndAssumptions_IncludesThemInList()
    {
        // Arrange
        var truthMap = new TruthMapModel
        {
            SessionId = TestSessionId,
            Risks = [new Agon.Domain.TruthMap.Entities.Risk("r-1", "Risk text", Agon.Domain.TruthMap.Entities.RiskCategory.Technical, Agon.Domain.TruthMap.Entities.RiskSeverity.High, Agon.Domain.TruthMap.Entities.RiskLikelihood.Medium, "Mitigation", [], "gpt_agent")],
            Assumptions = [new Agon.Domain.TruthMap.Entities.Assumption("a-1", "Assumption text", "Validate", [], Agon.Domain.TruthMap.Entities.AssumptionStatus.Unvalidated)]
        };

        var state = BuildSessionState(truthMap: truthMap);
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>()).Returns(state);

        // Act
        var result = await _controller.ListArtifacts(TestSessionId, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var artifacts = ok.Value.Should().BeAssignableTo<List<ArtifactResponse>>().Subject;
        artifacts.Should().Contain(a => a.Type == "risks");
        artifacts.Should().Contain(a => a.Type == "assumptions");
    }

    [Fact]
    public async Task ListArtifacts_WhenSessionDoesNotExist_Returns404()
    {
        // Arrange
        _sessionService.GetAsync(TestSessionId, Arg.Any<CancellationToken>())
            .Returns((SessionState?)null);

        // Act
        var result = await _controller.ListArtifacts(TestSessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── UploadAttachment with storage configured ──────────────────────────────

    [Fact]
    public async Task UploadAttachment_WhenFileSizeExceedsLimit_Returns400BadRequest()
    {
        // Arrange
        var mockStorage = Substitute.For<IAttachmentStorageService>();
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

        var mockFile = Substitute.For<IFormFile>();
        mockFile.Length.Returns(60 * 1024 * 1024L); // 60MB - exceeds 50MB limit
        mockFile.FileName.Returns("huge-file.pdf");
        mockFile.ContentType.Returns("application/pdf");

        var request = new UploadAttachmentRequest { File = mockFile };

        // Act
        var result = await controllerWithStorage.UploadAttachment(TestSessionId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
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
            UserId = Guid.Empty,
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
