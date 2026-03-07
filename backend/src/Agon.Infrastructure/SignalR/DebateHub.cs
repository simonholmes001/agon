using Microsoft.AspNetCore.SignalR;

namespace Agon.Infrastructure.SignalR;

/// <summary>
/// SignalR Hub for real-time debate session communication.
/// Clients connect to this hub and join session-specific groups to receive updates.
/// The SignalREventBroadcaster uses this hub's context to broadcast events.
/// </summary>
public sealed class DebateHub : Hub
{
    /// <summary>
    /// Called when a client connects and wants to join a specific session.
    /// Adds the connection to the session-specific group.
    /// </summary>
    public async Task JoinSession(Guid sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
    }

    /// <summary>
    /// Called when a client leaves a session.
    /// Removes the connection from the session-specific group.
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
}
