using Agon.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Agon.Infrastructure.SignalR;

/// <summary>
/// SignalR Hub for real-time debate session communication.
/// Clients connect to this hub and join session-specific groups to receive updates.
/// The SignalREventBroadcaster uses this hub's context to broadcast events.
/// </summary>
public sealed class DebateHub : Hub
{
    private readonly ISessionRepository _sessionRepository;
    private readonly ILogger<DebateHub> _logger;

    public DebateHub(ISessionRepository sessionRepository, ILogger<DebateHub> logger)
    {
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    /// <summary>
    /// Called when a client connects and wants to join a specific session.
    /// Verifies that the caller owns the session before adding to the group.
    /// Denies the join and returns an error if the session is not found or does not belong to the caller.
    /// </summary>
    public async Task JoinSession(Guid sessionId)
    {
        var callerId = ResolveCallerId();

        // Load the session to verify ownership
        var session = await _sessionRepository.GetAsync(sessionId);
        if (session == null || session.UserId != callerId)
        {
            // Always respond with the same message to avoid leaking existence of sessions
            _logger.LogWarning(
                "Denied SignalR group join attempt. ConnectionId={ConnectionId}, CallerIdHash={CallerIdHash}",
                Context.ConnectionId,
                HashForLog(callerId.ToString()));

            throw new HubException("Session not found or access denied.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
    }

    /// <summary>
    /// Called when a client leaves a session.
    /// Removes the connection from the session-specific group.
    /// No ownership check needed for leave — only the caller's own connection is affected.
    /// </summary>
    public async Task LeaveSession(Guid sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session:{sessionId}");
    }

    /// <summary>
    /// Automatically called when a client disconnects.
    /// SignalR handles group cleanup automatically.
    /// </summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR automatically removes the connection from all groups
        return base.OnDisconnectedAsync(exception);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the caller's user ID from the hub connection identity.
    /// Returns <see cref="Guid.Empty"/> when the caller is unauthenticated
    /// (allowed only in local-dev / auth-disabled configurations).
    /// </summary>
    private Guid ResolveCallerId()
    {
        var user = Context.User;
        if (user == null) return Guid.Empty;

        var claimValue =
            user.FindFirst("oid")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;

        if (!string.IsNullOrWhiteSpace(claimValue))
        {
            if (Guid.TryParse(claimValue, out var parsed))
                return parsed;

            // Deterministic GUID from non-GUID subject claim.
            // Namespace-prefixed to avoid cross-provider collisions if different IdPs share claim formats.
            var namespaced = $"agon:sub:{claimValue}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(namespaced));
            var guidBytes = new byte[16];
            Array.Copy(bytes, guidBytes, 16);
            return new Guid(guidBytes);
        }

        return Guid.Empty;
    }

    /// <summary>
    /// Returns a one-way hash of a value for safe structured logging (no PII).
    /// </summary>
    private static string HashForLog(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..8];
    }
}
