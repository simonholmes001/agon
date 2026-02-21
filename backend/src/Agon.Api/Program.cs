using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Services;
using Agon.Domain.Sessions;
using Agon.Infrastructure.Persistence.InMemory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ISessionRepository, InMemorySessionRepository>();
builder.Services.AddSingleton<ITruthMapRepository, InMemoryTruthMapRepository>();
builder.Services.AddSingleton<Orchestrator>();
builder.Services.AddSingleton<SessionService>();

var app = builder.Build();

app.MapPost("/sessions", async (
    CreateSessionRequest request,
    SessionService sessionService,
    CancellationToken cancellationToken) =>
{
    if (!Enum.TryParse<SessionMode>(request.Mode, ignoreCase: true, out var mode))
    {
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
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/sessions/{sessionId:guid}", async (
    Guid sessionId,
    SessionService sessionService,
    CancellationToken cancellationToken) =>
{
    var session = await sessionService.GetSessionAsync(sessionId, cancellationToken);
    if (session is null)
    {
        return Results.NotFound();
    }

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
    CancellationToken cancellationToken) =>
{
    try
    {
        var session = await sessionService.StartSessionAsync(sessionId, cancellationToken);
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
        return Results.NotFound();
    }
});

app.MapGet("/sessions/{sessionId:guid}/truthmap", async (
    Guid sessionId,
    SessionService sessionService,
    CancellationToken cancellationToken) =>
{
    var map = await sessionService.GetTruthMapAsync(sessionId, cancellationToken);
    if (map is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new TruthMapResponse(
        map.SessionId,
        map.Version,
        map.Round,
        map.CoreIdea));
});

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
