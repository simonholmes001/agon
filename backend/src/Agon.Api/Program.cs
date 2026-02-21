using Agon.Api.Middleware;
using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Services;
using Agon.Domain.Sessions;
using Agon.Infrastructure.Persistence.InMemory;
using Agon.Infrastructure.SignalR;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<ISessionRepository, InMemorySessionRepository>();
builder.Services.AddSingleton<ITruthMapRepository, InMemoryTruthMapRepository>();
builder.Services.AddSingleton<IEventBroadcaster, SignalREventBroadcaster>();
builder.Services.AddSingleton<Orchestrator>();
builder.Services.AddSingleton<AgentRunner>();
builder.Services.AddSingleton<SessionService>();

var app = builder.Build();
app.UseMiddleware<GlobalExceptionMiddleware>();

app.MapPost("/sessions", async (
    CreateSessionRequest request,
    SessionService sessionService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("SessionsApi");
    logger.LogInformation(
        "POST /sessions request received. Mode={Mode} FrictionLevel={FrictionLevel}",
        request.Mode,
        request.FrictionLevel);

    if (!Enum.TryParse<SessionMode>(request.Mode, ignoreCase: true, out var mode))
    {
        logger.LogWarning(
            "POST /sessions rejected due to invalid mode. Mode={Mode}",
            request.Mode);
        return Results.BadRequest(new { error = $"Invalid mode '{request.Mode}'." });
    }

    try
    {
        var session = await sessionService.CreateSessionAsync(
            request.Idea,
            mode,
            request.FrictionLevel,
            cancellationToken);

        return Results.Created($"/sessions/{session.SessionId}", new SessionResponse(
            session.SessionId,
            session.Status.ToString(),
            session.Phase.ToString(),
            session.Mode.ToString(),
            session.FrictionLevel,
            session.RoundNumber,
            session.TargetedLoopCount));
    }
    catch (ArgumentException exception)
    {
        logger.LogWarning(
            exception,
            "POST /sessions validation failed.");
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "POST /sessions failed unexpectedly.");
        throw;
    }
});

app.MapGet("/sessions/{sessionId:guid}", async (
    Guid sessionId,
    SessionService sessionService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("SessionsApi");
    var session = await sessionService.GetSessionAsync(sessionId, cancellationToken);
    if (session is null)
    {
        logger.LogWarning("GET /sessions/{SessionId} returned not found.", sessionId);
        return Results.NotFound();
    }

    logger.LogInformation(
        "GET /sessions/{SessionId} returned phase {Phase}.",
        sessionId,
        session.Phase);
    return Results.Ok(new SessionResponse(
        session.SessionId,
        session.Status.ToString(),
        session.Phase.ToString(),
        session.Mode.ToString(),
        session.FrictionLevel,
        session.RoundNumber,
        session.TargetedLoopCount));
});

app.MapPost("/sessions/{sessionId:guid}/start", async (
    Guid sessionId,
    SessionService sessionService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("SessionsApi");
    logger.LogInformation("POST /sessions/{SessionId}/start received.", sessionId);

    try
    {
        var session = await sessionService.StartSessionAsync(sessionId, cancellationToken);
        logger.LogInformation(
            "POST /sessions/{SessionId}/start transitioned to phase {Phase}.",
            sessionId,
            session.Phase);
        return Results.Ok(new SessionResponse(
            session.SessionId,
            session.Status.ToString(),
            session.Phase.ToString(),
            session.Mode.ToString(),
            session.FrictionLevel,
            session.RoundNumber,
            session.TargetedLoopCount));
    }
    catch (KeyNotFoundException)
    {
        logger.LogWarning("POST /sessions/{SessionId}/start returned not found.", sessionId);
        return Results.NotFound();
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "POST /sessions/{SessionId}/start failed unexpectedly.", sessionId);
        throw;
    }
});

app.MapGet("/sessions/{sessionId:guid}/truthmap", async (
    Guid sessionId,
    SessionService sessionService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("SessionsApi");
    var map = await sessionService.GetTruthMapAsync(sessionId, cancellationToken);
    if (map is null)
    {
        logger.LogWarning("GET /sessions/{SessionId}/truthmap returned not found.", sessionId);
        return Results.NotFound();
    }

    logger.LogInformation(
        "GET /sessions/{SessionId}/truthmap returned version {Version}.",
        sessionId,
        map.Version);
    return Results.Ok(new TruthMapResponse(
        map.SessionId,
        map.Version,
        map.Round,
        map.CoreIdea));
});

app.MapHub<DebateHub>("/hubs/debate");

app.Run();

public record CreateSessionRequest(string Idea, string Mode, int FrictionLevel);

public record SessionResponse(
    Guid SessionId,
    string Status,
    string Phase,
    string Mode,
    int FrictionLevel,
    int RoundNumber,
    int TargetedLoopCount);

public record TruthMapResponse(
    Guid SessionId,
    int Version,
    int Round,
    string CoreIdea);

public partial class Program;
