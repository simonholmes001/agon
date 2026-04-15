using Agon.Api.Observability;
using Agon.Api.Services;
using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Services;
using Agon.Domain.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Agon.Api.Controllers;

/// <summary>
/// RESTful endpoints for session lifecycle management.
/// Thin controller — all logic in Application layer.
/// </summary>
[ApiController]
[Route("[controller]")]
public class SessionsController : ControllerBase
{
    private const long MaxAttachmentSizeBytes = 25 * 1024 * 1024; // 25 MB
    private const int AttachmentPreviewChars = 500;
    private const string AttachmentStorageNotConfiguredCode = "ATTACHMENT_STORAGE_NOT_CONFIGURED";
    private const string AttachmentStorageUnavailableCode = "ATTACHMENT_STORAGE_UNAVAILABLE";
    private const string AttachmentMetadataNotConfiguredCode = "ATTACHMENT_METADATA_NOT_CONFIGURED";
    private const string AttachmentMetadataUnavailableCode = "ATTACHMENT_METADATA_UNAVAILABLE";
    private const string EntraGroupsClaimType = "groups";
    private const string LegacyGroupsClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/groups";

    private readonly ISessionService _sessionService;
    private readonly IAttachmentStorageService? _attachmentStorage;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ConversationHistoryService _conversationHistory;
    private readonly ILogger<SessionsController> _logger;
    private readonly TrialAccessService _trialAccessService;

    public SessionsController(
        ISessionService sessionService,
        IServiceScopeFactory serviceScopeFactory,
        ConversationHistoryService conversationHistory,
        ILogger<SessionsController> logger,
        TrialAccessService trialAccessService,
        IAttachmentStorageService? attachmentStorage = null)
    {
        _sessionService = sessionService;
        _attachmentStorage = attachmentStorage;
        _serviceScopeFactory = serviceScopeFactory;
        _conversationHistory = conversationHistory;
        _logger = logger;
        _trialAccessService = trialAccessService;
    }

    /// <summary>
    /// POST /sessions — Creates a new debate session.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("session-create")]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSession(
        [FromBody] CreateSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Idea))
        {
            return BadRequest(new { error = "Idea is required" });
        }

        if (request.FrictionLevel < 0 || request.FrictionLevel > 100)
        {
            return BadRequest(new { error = "FrictionLevel must be between 0 and 100" });
        }

        var userId = ResolveCurrentUserId();
        var access = await EvaluateTrialAccessAsync(
            userId,
            TrialAccessOperation.SessionCreate,
            cancellationToken);
        if (!access.Allowed)
        {
            return BuildTrialDeniedResult(access);
        }

        var sessionState = await _sessionService.CreateAsync(
            userId,
            request.Idea,
            request.FrictionLevel,
            cancellationToken);

        var response = MapToResponse(sessionState);

        _logger.LogInformation(
            "Created session {SessionId} for user {UserId}",
            sessionState.SessionId,
            userId);

        return CreatedAtAction(
            nameof(GetSession),
            new { id = sessionState.SessionId },
            response);
    }

    /// <summary>
    /// GET /sessions — Lists sessions for the current user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SessionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSessions(CancellationToken cancellationToken)
    {
        var userId = ResolveCurrentUserId();
        var sessions = await _sessionService.ListByUserAsync(userId, cancellationToken);
        var response = sessions.Select(MapToResponse).ToList();
        return Ok(response);
    }

    /// <summary>
    /// GET /sessions/{id} — Retrieves session state.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var userId = ResolveCurrentUserId();
        var sessionState = await _sessionService.GetByUserAsync(id, userId, cancellationToken);

        if (sessionState == null)
        {
            return NotFound(new { error = $"Session {id} not found" });
        }

        var response = MapToResponse(sessionState);
        return Ok(response);
    }

    /// <summary>
    /// POST /sessions/{id}/start — Begins clarification phase.
    /// </summary>
    [HttpPost("{id}/start")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartDebate(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var userId = ResolveCurrentUserId();
        var session = await _sessionService.GetByUserAsync(id, userId, cancellationToken);
        if (session is null)
        {
            return NotFound(new { error = $"Session {id} not found" });
        }

        var access = await EvaluateTrialAccessAsync(
            session.UserId,
            TrialAccessOperation.SessionMessage,
            cancellationToken);
        if (!access.Allowed)
        {
            return BuildTrialDeniedResult(access);
        }

        try
        {
            await _sessionService.StartClarificationAsync(id, cancellationToken);

            _logger.LogInformation("Started clarification for session {SessionId}", id);

            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to start session {SessionId}", id);
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /sessions/{id}/messages — Submit a user message (clarification response or post-delivery question).
    /// </summary>
    [HttpPost("{id}/messages")]
    [EnableRateLimiting("session-message")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitMessage(
        [FromRoute] Guid id,
        [FromBody] MessageRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { error = "Message content is required" });
        }

        var userId = ResolveCurrentUserId();
        var session = await _sessionService.GetByUserAsync(id, userId, cancellationToken);
        if (session is null)
        {
            return NotFound(new { error = $"Session {id} not found" });
        }

        var access = await EvaluateTrialAccessAsync(
            session.UserId,
            TrialAccessOperation.SessionMessage,
            cancellationToken);
        if (!access.Allowed)
        {
            return BuildTrialDeniedResult(access);
        }

        try
        {
            await _sessionService.SubmitMessageAsync(id, request.Content, cancellationToken);

            _logger.LogInformation(
                "Submitted message for session {SessionId} (MessageLength={MessageLength})",
                id,
                request.Content.Length);

            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to submit message to session {SessionId}", id);
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// GET /sessions/{id}/truthmap — Retrieve the current Truth Map state.
    /// </summary>
    [HttpGet("{id}/truthmap")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTruthMap(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var userId = ResolveCurrentUserId();
        var sessionState = await _sessionService.GetByUserAsync(id, userId, cancellationToken);

        if (sessionState is null)
        {
            return NotFound(new { error = $"Session {id} not found" });
        }

        // Return the Truth Map as JSON
        return Ok(sessionState.TruthMap);
    }

    /// <summary>
    /// GET /sessions/{id}/snapshots — List available round snapshots for Pause-and-Replay.
    /// </summary>
    [HttpGet("{id}/snapshots")]
    [ProducesResponseType(typeof(IReadOnlyList<SnapshotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListSnapshots(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var userId = ResolveCurrentUserId();
        var sessionState = await _sessionService.GetByUserAsync(id, userId, cancellationToken);
        if (sessionState is null)
        {
            return NotFound(new { error = $"Session {id} not found" });
        }

        var snapshots = await _sessionService.ListSnapshotsAsync(id, cancellationToken);

        var response = snapshots.Select(s => new SnapshotResponse(
            s.SnapshotId,
            s.Round,
            s.CreatedAt
        )).ToList();

        return Ok(response);
    }

    /// <summary>
    /// POST /sessions/test-agent — Simple endpoint to test agent functionality with a direct question.
    /// This bypasses the full orchestration workflow and directly invokes a single agent.
    /// </summary>
    [HttpPost("test-agent")]
    [ProducesResponseType(typeof(AgentTestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TestAgent(
        [FromBody] AgentTestRequest request,
        [FromServices] IReadOnlyList<Application.Interfaces.ICouncilAgent> agents,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get the first available agent (Moderator)
            var agent = agents.FirstOrDefault();
            
            if (agent is null)
            {
                return StatusCode(500, new { error = "No agents configured" });
            }

            _logger.LogInformation(
                "Testing agent {AgentId} (QuestionLength={QuestionLength})",
                agent.AgentId,
                request.Question?.Length ?? 0);

            // Create a Truth Map seeded with the user's question as CoreIdea
            var sessionId = Guid.NewGuid();
            var truthMap = Domain.TruthMap.TruthMap.Empty(sessionId) with 
            { 
                CoreIdea = request.Question 
            };
            
            var context = Application.Models.AgentContext.ForAnalysis(
                sessionId: sessionId,
                truthMap: truthMap,
                frictionLevel: 50,
                roundNumber: 1,
                researchToolsEnabled: false
            );

            // Invoke the agent
            var response = await agent.RunAsync(context, cancellationToken);

            _logger.LogInformation(
                "Agent {AgentId} responded (MessageLength={MessageLength})",
                agent.AgentId,
                response.Message?.Length ?? 0);

            var patchCount = response.Patch != null ? 1 : 0;

            return Ok(new AgentTestResponse(
                agent.AgentId,
                response.Message,
                patchCount
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test agent");
            return StatusCode(500, new { error = "Agent test failed." });
        }
    }

    // ── Helper Methods ──────────────────────────────────────────────────

    /// <summary>
    /// GET /sessions/{id}/messages — Retrieves conversation history (agent messages).
    /// </summary>
    [HttpGet("{id}/messages")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMessages(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var userId = ResolveCurrentUserId();
        var session = await _sessionService.GetByUserAsync(id, userId, cancellationToken);
        if (session is null)
        {
            return NotFound(new { error = $"Session {id} not found" });
        }

        var messages = await _conversationHistory.GetMessagesAsync(id, cancellationToken);
        
        var response = messages.Select(m => new MessageResponse(
            m.AgentId,
            m.Message,
            m.Round,
            m.CreatedAt))
            .ToList();
        
        _logger.LogInformation(
            "Retrieved {Count} messages for session {SessionId}",
            response.Count,
            id);
        
        return Ok(response);
    }

    /// <summary>
    /// POST /sessions/{id}/attachments — Upload any document/image to the active discussion.
    /// Metadata is persisted, file is stored in blob storage, and extracted text (when available)
    /// is injected into subsequent agent context.
    /// Requires an authenticated user when authentication is enabled (Authentication:Enabled=true);
    /// enforced globally by the application's authorization policy.
    /// </summary>
    [HttpPost("{id}/attachments")]
    [EnableRateLimiting("attachment-upload")]
    [RequestSizeLimit(MaxAttachmentSizeBytes)]
    [ProducesResponseType(typeof(SessionAttachmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UploadAttachment(
        [FromRoute] Guid id,
        [FromForm] UploadAttachmentRequest request,
        CancellationToken cancellationToken)
    {
        if (_attachmentStorage is null)
        {
            AttachmentMetrics.UploadFailure.Add(1);
            return AttachmentServiceUnavailable(
                AttachmentStorageNotConfiguredCode,
                "Attachment storage is not configured.",
                "Set ConnectionStrings:BlobStorage and restart the backend.");
        }

        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest(new { error = "Attachment file is required." });
        }

        if (request.File.Length > MaxAttachmentSizeBytes)
        {
            return BadRequest(new { error = $"Attachment exceeds max size of {MaxAttachmentSizeBytes / (1024 * 1024)} MB." });
        }

        var session = await _sessionService.GetByUserAsync(id, ResolveCurrentUserId(), cancellationToken);
        if (session is null)
        {
            return NotFound(new { error = $"Session {id} not found" });
        }

        var safeFileName = SanitizeFileName(request.File.FileName);
        var contentType = string.IsNullOrWhiteSpace(request.File.ContentType)
            ? GuessContentType(safeFileName)
            : request.File.ContentType.Trim();

        var blobName = $"{id:N}/{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}-{safeFileName}";
        AttachmentUploadResult uploaded;
        try
        {
            await using var uploadStream = request.File.OpenReadStream();
            uploaded = await _attachmentStorage.UploadAsync(
                blobName,
                uploadStream,
                contentType,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AttachmentMetrics.UploadFailure.Add(1);
            _logger.LogError(
                ex,
                "Attachment upload failed for session {SessionId}. ErrorCode={ErrorCode}, FileName={FileName}, ContentType={ContentType}, SizeBytes={SizeBytes}",
                id,
                AttachmentStorageUnavailableCode,
                safeFileName,
                contentType,
                request.File.Length);
            return AttachmentServiceUnavailable(
                AttachmentStorageUnavailableCode,
                "Attachment storage is temporarily unavailable.",
                "Verify blob storage connectivity and retry.");
        }

        var now = DateTimeOffset.UtcNow;
        var attachmentId = Guid.NewGuid();
        var attachment = new SessionAttachment(
            AttachmentId: attachmentId,
            SessionId: id,
            UserId: session.UserId,
            FileName: safeFileName,
            ContentType: contentType,
            SizeBytes: request.File.Length,
            BlobName: uploaded.BlobName,
            BlobUri: uploaded.BlobUri,
            AccessUrl: BuildAttachmentDownloadPath(id, attachmentId),
            ExtractedText: null,
            UploadedAt: now,
            ExtractionStatus: AttachmentExtractionStatus.Queued,
            ExtractionProgressPercent: 0,
            ExtractionError: null,
            ExtractionUpdatedAt: now);

        SessionAttachment saved;
        try
        {
            saved = await _sessionService.SaveAttachmentAsync(attachment, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            AttachmentMetrics.UploadFailure.Add(1);
            _logger.LogWarning(
                ex,
                "Attachment metadata persistence is not configured for session {SessionId}. ErrorCode={ErrorCode}",
                id,
                AttachmentMetadataNotConfiguredCode);
            return AttachmentServiceUnavailable(
                AttachmentMetadataNotConfiguredCode,
                "Attachment metadata persistence is not configured.",
                "Enable attachment repository persistence and retry.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AttachmentMetrics.UploadFailure.Add(1);
            _logger.LogError(
                ex,
                "Attachment metadata persistence failed for session {SessionId}. ErrorCode={ErrorCode}, FileName={FileName}",
                id,
                AttachmentMetadataUnavailableCode,
                safeFileName);
            return AttachmentServiceUnavailable(
                AttachmentMetadataUnavailableCode,
                "Attachment metadata persistence is temporarily unavailable.",
                "Check database connectivity and retry.");
        }

        _logger.LogInformation(
            "Uploaded attachment {AttachmentId} for session {SessionId} (FileName={FileName}, ContentType={ContentType}, SizeBytes={SizeBytes}, ExtractionStatus={ExtractionStatus})",
            saved.AttachmentId,
            id,
            saved.FileName,
            saved.ContentType,
            saved.SizeBytes,
            saved.ExtractionStatus);
        AttachmentMetrics.UploadSuccess.Add(1);
        QueueAttachmentExtraction(saved);

        return CreatedAtAction(
            nameof(ListAttachments),
            new { id },
            MapAttachmentResponse(saved));
    }

    /// <summary>
    /// GET /sessions/{id}/attachments — List all files attached to a session.
    /// Requires an authenticated user when authentication is enabled (Authentication:Enabled=true);
    /// enforced globally by the application's authorization policy.
    /// </summary>
    [HttpGet("{id}/attachments")]
    [ProducesResponseType(typeof(IReadOnlyList<SessionAttachmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAttachments(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var session = await _sessionService.GetByUserAsync(id, ResolveCurrentUserId(), cancellationToken);
        if (session is null)
        {
            return NotFound(new { error = $"Session {id} not found" });
        }

        var attachments = await _sessionService.ListAttachmentsAsync(id, cancellationToken);
        var response = attachments.Select(MapAttachmentResponse).ToList();
        return Ok(response);
    }

    /// <summary>
    /// GET /sessions/{id}/attachments/{attachmentId}/content — Streams attachment content for authorized session owner.
    /// </summary>
    [HttpGet("{id}/attachments/{attachmentId}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadAttachmentContent(
        [FromRoute] Guid id,
        [FromRoute] Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var session = await _sessionService.GetByUserAsync(id, ResolveCurrentUserId(), cancellationToken);
        if (session is null)
        {
            return NotFound(new { error = $"Session {id} not found" });
        }

        var attachments = await _sessionService.ListAttachmentsAsync(id, cancellationToken);
        var attachment = attachments.FirstOrDefault(a => a.AttachmentId == attachmentId);
        if (attachment is null)
        {
            return NotFound(new { error = $"Attachment {attachmentId} not found for session {id}" });
        }

        if (_attachmentStorage is null)
        {
            return AttachmentServiceUnavailable(
                AttachmentStorageNotConfiguredCode,
                "Attachment storage is not configured.",
                "Set ConnectionStrings:BlobStorage and restart the backend.");
        }

        try
        {
            var contentStream = await _attachmentStorage.OpenReadAsync(attachment.BlobName, cancellationToken);
            if (contentStream is null)
            {
                return NotFound(new { error = $"Attachment {attachmentId} content is not available" });
            }

            return File(contentStream, attachment.ContentType, attachment.FileName, enableRangeProcessing: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Attachment download failed for AttachmentId={AttachmentId}, SessionId={SessionId}, BlobName={BlobName}",
                attachmentId,
                id,
                attachment.BlobName);

            return AttachmentServiceUnavailable(
                AttachmentStorageUnavailableCode,
                "Attachment storage is temporarily unavailable.",
                "Verify blob storage connectivity and retry.");
        }
    }

    /// <summary>
    /// GET /sessions/{id}/artifacts/{type} — Retrieves a synthesized artifact from the debate.
    /// Maps artifact types to agent messages:
    ///   verdict   → synthesizer's final message
    ///   plan      → synthesizer's message with "plan" framing
    ///   risks     → claims/risks from Truth Map
    ///   assumptions → assumptions from Truth Map
    ///   (others)  → synthesizer message with type label
    /// </summary>
    [HttpGet("{id}/artifacts/{type}")]
    [ProducesResponseType(typeof(ArtifactResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetArtifact(
        [FromRoute] Guid id,
        [FromRoute] string type,
        CancellationToken cancellationToken)
    {
        var sessionState = await _sessionService.GetByUserAsync(id, ResolveCurrentUserId(), cancellationToken);
        if (sessionState is null)
            return NotFound(new { error = $"Session {id} not found" });

        var messages = await _conversationHistory.GetMessagesAsync(id, cancellationToken);

        string? content = type.ToLowerInvariant() switch
        {
            // Risks and assumptions come from the Truth Map
            "risks" => BuildRisksArtifact(sessionState.TruthMap),
            "assumptions" => BuildAssumptionsArtifact(sessionState.TruthMap),

            // Everything else comes from the synthesizer's most recent message
            _ => messages
                .Where(m => m.AgentId == "synthesizer")
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => m.Message)
                .FirstOrDefault()
        };

        if (string.IsNullOrWhiteSpace(content))
        {
            // Fall back to any agent message if synthesizer hasn't run yet
            var anyMessage = messages
                .Where(m => m.AgentId != "moderator")
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => $"**{m.AgentId}** (Round {m.Round}):\n\n{m.Message}")
                .FirstOrDefault();

            if (anyMessage is null)
            {
                return NotFound(new
                {
                    error = $"Artifact '{type}' is not yet available. The debate may still be in progress.",
                    hint = "Run 'agon status' to check debate progress."
                });
            }

            content = anyMessage;
        }

        return Ok(new ArtifactResponse(
            Type: type,
            Content: content,
            Version: sessionState.TruthMap.Version,
            CreatedAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// GET /sessions/{id}/artifacts — Lists available artifacts for a session.
    /// </summary>
    [HttpGet("{id}/artifacts")]
    [ProducesResponseType(typeof(IReadOnlyList<ArtifactResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListArtifacts(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var messages = await _conversationHistory.GetMessagesAsync(id, cancellationToken);
        var sessionState = await _sessionService.GetByUserAsync(id, ResolveCurrentUserId(), cancellationToken);
        if (sessionState is null)
            return NotFound(new { error = $"Session {id} not found" });

        var artifacts = new List<ArtifactResponse>();

        var synthMessage = messages
            .Where(m => m.AgentId == "synthesizer")
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        if (synthMessage is not null)
        {
            var now = synthMessage.CreatedAt;
            artifacts.Add(new ArtifactResponse("verdict", synthMessage.Message, sessionState.TruthMap.Version, now));
            artifacts.Add(new ArtifactResponse("plan", synthMessage.Message, sessionState.TruthMap.Version, now));
        }

        var risksContent = BuildRisksArtifact(sessionState.TruthMap);
        if (!string.IsNullOrWhiteSpace(risksContent))
            artifacts.Add(new ArtifactResponse("risks", risksContent, sessionState.TruthMap.Version, DateTimeOffset.UtcNow));

        var assumptionsContent = BuildAssumptionsArtifact(sessionState.TruthMap);
        if (!string.IsNullOrWhiteSpace(assumptionsContent))
            artifacts.Add(new ArtifactResponse("assumptions", assumptionsContent, sessionState.TruthMap.Version, DateTimeOffset.UtcNow));

        return Ok(artifacts);
    }

    private static string BuildRisksArtifact(Domain.TruthMap.TruthMap truthMap)
    {
        if (!truthMap.Risks.Any()) return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Risk Analysis\n");
        foreach (var risk in truthMap.Risks)
        {
            sb.AppendLine($"## Risk: {risk.Id}");
            sb.AppendLine($"**Severity:** {risk.Severity}  |  **Likelihood:** {risk.Likelihood}  |  **Category:** {risk.Category}");
            sb.AppendLine($"\n{risk.Text}\n");
            if (!string.IsNullOrWhiteSpace(risk.Mitigation))
                sb.AppendLine($"**Mitigation:** {risk.Mitigation}\n");
        }
        return sb.ToString();
    }

    private static string BuildAssumptionsArtifact(Domain.TruthMap.TruthMap truthMap)
    {
        if (!truthMap.Assumptions.Any()) return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Assumptions\n");
        foreach (var assumption in truthMap.Assumptions)
        {
            sb.AppendLine($"- **{assumption.Text}**");
            sb.AppendLine($"  Status: {assumption.Status} | Validation: {assumption.ValidationStep}\n");
        }
        return sb.ToString();
    }

    private void QueueAttachmentExtraction(SessionAttachment attachment)
    {
        _ = Task.Run(async () =>
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<AttachmentExtractionProcessor>();

            try
            {
                await processor.ProcessAsync(attachment, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Background attachment extraction failed unexpectedly for AttachmentId={AttachmentId}, SessionId={SessionId}",
                    attachment.AttachmentId,
                    attachment.SessionId);
            }
        });
    }

    private ObjectResult AttachmentServiceUnavailable(string errorCode, string message, string hint)
    {
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            errorCode,
            error = message,
            hint
        });
    }

    private static SessionResponse MapToResponse(Application.Models.SessionState sessionState)
    {
        return new SessionResponse(
            sessionState.SessionId,
            sessionState.Phase.ToString(),
            sessionState.Status.ToString(),
            sessionState.FrictionLevel,
            sessionState.CurrentRound,
            sessionState.TokensUsed,
            sessionState.CreatedAt,
            sessionState.UpdatedAt);
    }

    private static SessionAttachmentResponse MapAttachmentResponse(SessionAttachment attachment)
    {
        var preview = string.IsNullOrWhiteSpace(attachment.ExtractedText)
            ? null
            : attachment.ExtractedText[..Math.Min(attachment.ExtractedText.Length, AttachmentPreviewChars)];

        return new SessionAttachmentResponse(
            attachment.AttachmentId,
            attachment.SessionId,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            BuildAttachmentDownloadPath(attachment.SessionId, attachment.AttachmentId),
            attachment.UploadedAt,
            attachment.ExtractionStatus,
            attachment.ExtractionProgressPercent,
            attachment.ExtractionError,
            attachment.ExtractionUpdatedAt,
            !string.IsNullOrWhiteSpace(attachment.ExtractedText),
            preview);
    }

    private static string BuildAttachmentDownloadPath(Guid sessionId, Guid attachmentId) =>
        $"/sessions/{sessionId}/attachments/{attachmentId}/content";

    private async Task<TrialAccessResult> EvaluateTrialAccessAsync(
        Guid userId,
        TrialAccessOperation operation,
        CancellationToken cancellationToken)
    {
        return await _trialAccessService.EvaluateAsync(
            userId,
            ResolveCurrentUserGroupIds(),
            operation,
            cancellationToken);
    }

    private ObjectResult BuildTrialDeniedResult(TrialAccessResult result)
    {
        if (result.RetryAfterSeconds is > 0)
        {
            Response.Headers.RetryAfter = result.RetryAfterSeconds.Value.ToString();
        }

        return StatusCode(result.StatusCode, new
        {
            errorCode = result.ErrorCode,
            error = result.Error,
            limitType = result.LimitType,
            windowResetAt = result.WindowResetAt,
            remainingTokens = result.RemainingTokens
        });
    }

    private static string SanitizeFileName(string fileName)
    {
        var baseName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return $"attachment-{Guid.NewGuid():N}.bin";
        }

        var cleaned = Regex.Replace(baseName.Trim(), @"\s+", " ");
        cleaned = Regex.Replace(cleaned, @"[^a-zA-Z0-9._ -]", "-");
        return string.IsNullOrWhiteSpace(cleaned)
            ? $"attachment-{Guid.NewGuid():N}.bin"
            : cleaned;
    }

    private static string GuessContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".md" or ".markdown" => "text/markdown",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".xml" => "application/xml",
            ".yaml" or ".yml" => "application/x-yaml",
            ".html" or ".htm" => "text/html",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".jfif" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Resolves the current user's identity to a stable <see cref="Guid"/>.
    /// </summary>
    /// <remarks>
    /// Claim resolution order (first non-empty claim wins):
    /// <list type="number">
    ///   <item><description><c>oid</c> — Azure AD object identifier (already a GUID string)</description></item>
    ///   <item><description><c>nameidentifier</c> (<see cref="ClaimTypes.NameIdentifier"/>) — standard identity claim</description></item>
    ///   <item><description><c>sub</c> — OAuth 2.0 subject claim</description></item>
    /// </list>
    /// If the resolved claim value is already a valid GUID it is parsed directly.
    /// Otherwise a deterministic version-3-style GUID is derived from the claim string
    /// via SHA-256 so that the same principal always maps to the same user ID.
    /// <para>
    /// Returns <see cref="Guid.Empty"/> only when no identity claim is present at all
    /// (i.e. the caller is unauthenticated). This value is used exclusively in
    /// local-dev / auth-disabled scenarios where all sessions are created anonymously.
    /// When authentication is enabled, the global authorization policy (set via
    /// <c>Authentication:Enabled=true</c>) ensures unauthenticated requests are rejected
    /// before reaching the controller.
    /// </para>
    /// </remarks>
    private Guid ResolveCurrentUserId()
    {
        var claimValue =
            User.FindFirstValue("oid")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!string.IsNullOrWhiteSpace(claimValue))
        {
            if (Guid.TryParse(claimValue, out var parsed))
            {
                return parsed;
            }

            return DeterministicGuidFromString(claimValue);
        }

        return Guid.Empty;
    }

    private IReadOnlyCollection<string> ResolveCurrentUserGroupIds()
    {
        var groups = User.Claims
            .Where(claim =>
                string.Equals(claim.Type, EntraGroupsClaimType, StringComparison.OrdinalIgnoreCase)
                || string.Equals(claim.Type, LegacyGroupsClaimType, StringComparison.OrdinalIgnoreCase))
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return groups;
    }

    private static Guid DeterministicGuidFromString(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the current request's resolved user ID
    /// matches the session's owner.
    /// </summary>
    /// <remarks>
    /// Authenticated users are always matched against the session's stored
    /// <see cref="Application.Models.SessionState.UserId"/> — there is no fallback that
    /// grants access to all sessions.  This ensures that even when authentication is
    /// disabled (local-dev mode), an anonymous caller with <see cref="Guid.Empty"/> as
    /// their user ID can only reach sessions that were themselves created anonymously
    /// (i.e. sessions whose <see cref="Application.Models.SessionState.UserId"/> is also
    /// <see cref="Guid.Empty"/>).
    /// </remarks>
    private bool IsOwnedByCurrentUser(Application.Models.SessionState state)
    {
        var userId = ResolveCurrentUserId();
        if (User.Identity?.IsAuthenticated == true && userId == Guid.Empty)
        {
            // Authenticated principal but no recognisable identity claim — deny.
            return false;
        }

        // Strict equality: anonymous (Guid.Empty) users may only access anonymous sessions.
        return state.UserId == userId;
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────

public record CreateSessionRequest(string Idea, int FrictionLevel);

public record MessageRequest(string Content);

public sealed class UploadAttachmentRequest
{
    public IFormFile? File { get; init; }
}

public record SessionResponse(
    Guid Id,
    string Phase,
    string Status,
    int FrictionLevel,
    int CurrentRound,
    int TokensUsed,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record SnapshotResponse(
    Guid SnapshotId,
    int Round,
    DateTimeOffset CreatedAt);

public record AgentTestRequest(string Question);

public record AgentTestResponse(string AgentId, string Message, int PatchOperationsCount);

public record MessageResponse(
    string AgentId,
    string Message,
    int Round,
    DateTimeOffset CreatedAt);

public record ArtifactResponse(
    string Type,
    string Content,
    int Version,
    DateTimeOffset CreatedAt);

public record SessionAttachmentResponse(
    Guid Id,
    Guid SessionId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string AccessUrl,
    DateTimeOffset UploadedAt,
    string ExtractionStatus,
    int ExtractionProgressPercent,
    string? ExtractionError,
    DateTimeOffset? ExtractionUpdatedAt,
    bool HasExtractedText,
    string? ExtractedTextPreview);
