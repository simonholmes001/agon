using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Agon.Infrastructure.SignalR;

public class DebateHub(ILogger<DebateHub> logger) : Hub
{
    private const string SessionGroupPrefix = "session:";

    public static string SessionGroupName(Guid sessionId) => $"{SessionGroupPrefix}{sessionId:D}";

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation(
            "SignalR client connected. ConnectionId={ConnectionId}",
            Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is null)
        {
            logger.LogInformation(
                "SignalR client disconnected cleanly. ConnectionId={ConnectionId}",
                Context.ConnectionId);
        }
        else
        {
            logger.LogWarning(
                exception,
                "SignalR client disconnected with error. ConnectionId={ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinSession(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var parsedSessionId))
        {
            logger.LogWarning(
                "Rejecting join for invalid session id. ConnectionId={ConnectionId} SessionId={SessionId}",
                Context.ConnectionId,
                sessionId);
            throw new HubException("Invalid session id.");
        }

        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroupName(parsedSessionId));
            logger.LogInformation(
                "SignalR client joined session group. ConnectionId={ConnectionId} SessionId={SessionId}",
                Context.ConnectionId,
                parsedSessionId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to add SignalR client to session group. ConnectionId={ConnectionId} SessionId={SessionId}",
                Context.ConnectionId,
                parsedSessionId);
            throw;
        }
    }

    public async Task LeaveSession(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var parsedSessionId))
        {
            logger.LogWarning(
                "Rejecting leave for invalid session id. ConnectionId={ConnectionId} SessionId={SessionId}",
                Context.ConnectionId,
                sessionId);
            return;
        }

        try
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, SessionGroupName(parsedSessionId));
            logger.LogInformation(
                "SignalR client left session group. ConnectionId={ConnectionId} SessionId={SessionId}",
                Context.ConnectionId,
                parsedSessionId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to remove SignalR client from session group. ConnectionId={ConnectionId} SessionId={SessionId}",
                Context.ConnectionId,
                parsedSessionId);
            throw;
        }
    }
}
