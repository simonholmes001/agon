using Agon.Application.Interfaces;
using Agon.Application.Services;
using Agon.Domain.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace Agon.Api.Controllers;

/// <summary>
/// RESTful endpoints for session lifecycle management.
/// Thin controller — all logic in Application layer.
/// </summary>
[ApiController]
[Route("[controller]")]
public class SessionsController : ControllerBase
{
    private readonly ISessionService _sessionService;
    private readonly ConversationHistoryService _conversationHistory;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        ISessionService sessionService,
        ConversationHistoryService conversationHistory,
        ILogger<SessionsController> logger)
    {
        _sessionService = sessionService;
        _conversationHistory = conversationHistory;
        _logger = logger;
    }

    /// <summary>
    /// POST /sessions — Creates a new debate session.
    /// </summary>
    [HttpPost]
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

        // TODO: Get actual userId from auth context
        var userId = Guid.NewGuid();

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
    /// GET /sessions/{id} — Retrieves session state.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var sessionState = await _sessionService.GetAsync(id, cancellationToken);

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

        try
        {
            await _sessionService.SubmitMessageAsync(id, request.Content, cancellationToken);

            _logger.LogInformation(
                "Submitted message for session {SessionId}: {Content}",
                id,
                request.Content);

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
        var sessionState = await _sessionService.GetAsync(id, cancellationToken);

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

            _logger.LogInformation("Testing agent {AgentId} with question: {Question}", agent.AgentId, request.Question);

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

            _logger.LogInformation("Agent {AgentId} responded: {Message}", agent.AgentId, response.Message);

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
            return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
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
            sessionState.CreatedAt); // TODO: Add UpdatedAt to SessionState
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────

public record CreateSessionRequest(string Idea, int FrictionLevel);

public record MessageRequest(string Content);

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
