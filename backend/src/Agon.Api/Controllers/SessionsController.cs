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
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        ISessionService sessionService,
        ILogger<SessionsController> logger)
    {
        _sessionService = sessionService;
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
    public async Task<IActionResult> SubmitMessage(
        [FromRoute] Guid id,
        [FromBody] MessageRequest request,
        CancellationToken cancellationToken)
    {
        var sessionState = await _sessionService.GetAsync(id, cancellationToken);

        if (sessionState is null)
        {
            return NotFound(new { error = $"Session {id} not found" });
        }

        // TODO: Route message to Orchestrator for processing
        // For now, just acknowledge receipt
        _logger.LogInformation(
            "Received message for session {SessionId}: {Content}",
            id,
            request.Content);

        return Accepted();
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

    // ── Helper Methods ──────────────────────────────────────────────────

    private static SessionResponse MapToResponse(Application.Models.SessionState sessionState)
    {
        return new SessionResponse(
            sessionState.SessionId,
            sessionState.Phase.ToString(),
            sessionState.Status.ToString(),
            sessionState.FrictionLevel,
            sessionState.CurrentRound,
            sessionState.TokensUsed);
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────

public record CreateSessionRequest(string Idea, int FrictionLevel);

public record MessageRequest(string Content);

public record SessionResponse(
    Guid SessionId,
    string Phase,
    string Status,
    int FrictionLevel,
    int RoundCount,
    int TokensUsed);

public record SnapshotResponse(
    Guid SnapshotId,
    int Round,
    DateTimeOffset CreatedAt);
