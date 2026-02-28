using Agon.Api.Contracts;
using Agon.Application.Services;
using Agon.Application.Sessions;
using Agon.Domain.Sessions;

namespace Agon.Api.Endpoints;

/// <summary>
/// Endpoint handlers for session API routes.
/// </summary>
public static class SessionEndpoints
{
    private const string SessionsApiLoggerCategory = "SessionsApi";
    private const string CorrelationHeaderName = "X-Correlation-ID";

    /// <summary>
    /// Maps all session-related endpoints to the application.
    /// </summary>
    public static void MapSessionEndpoints(this WebApplication app)
    {
        app.MapPost("/sessions", CreateSessionAsync);
        app.MapGet("/sessions/{sessionId:guid}", GetSessionAsync);
        app.MapPost("/sessions/{sessionId:guid}/start", StartSessionAsync);
        app.MapPost("/sessions/{sessionId:guid}/messages", PostMessageAsync);
        app.MapGet("/sessions/{sessionId:guid}/truthmap", GetTruthMapAsync);
        app.MapGet("/sessions/{sessionId:guid}/transcript", GetTranscriptAsync);
    }

    private static async Task<IResult> CreateSessionAsync(
        CreateSessionRequest request,
        SessionService sessionService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(SessionsApiLoggerCategory);
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

            return Results.Created($"/sessions/{session.SessionId}", ToResponse(session));
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                exception,
                "POST /sessions validation failed.");
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> GetSessionAsync(
        Guid sessionId,
        SessionService sessionService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(SessionsApiLoggerCategory);
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
        return Results.Ok(ToResponse(session));
    }

    private static async Task<IResult> StartSessionAsync(
        Guid sessionId,
        SessionService sessionService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(httpContext);
        httpContext.Response.Headers[CorrelationHeaderName] = correlationId;

        var logger = loggerFactory.CreateLogger(SessionsApiLoggerCategory);
        logger.LogInformation(
            "POST /sessions/{SessionId}/start received. CorrelationId={CorrelationId}",
            sessionId,
            correlationId);

        try
        {
            var session = await sessionService.StartSessionAsync(
                sessionId,
                cancellationToken,
                correlationId);
            logger.LogInformation(
                "POST /sessions/{SessionId}/start transitioned to phase {Phase}. CorrelationId={CorrelationId}",
                sessionId,
                session.Phase,
                correlationId);
            return Results.Ok(ToResponse(session));
        }
        catch (KeyNotFoundException exception)
        {
            logger.LogWarning(
                exception,
                "POST /sessions/{SessionId}/start returned not found. CorrelationId={CorrelationId}",
                sessionId,
                correlationId);
            return Results.NotFound();
        }
    }

    private static async Task<IResult> PostMessageAsync(
        Guid sessionId,
        PostSessionMessageRequest request,
        SessionService sessionService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(httpContext);
        httpContext.Response.Headers[CorrelationHeaderName] = correlationId;
        var logger = loggerFactory.CreateLogger(SessionsApiLoggerCategory);

        logger.LogInformation(
            "POST /sessions/{SessionId}/messages received. CorrelationId={CorrelationId}",
            sessionId,
            correlationId);

        try
        {
            var result = await sessionService.PostUserMessageAsync(
                sessionId,
                request.Message ?? string.Empty,
                cancellationToken,
                correlationId);

            logger.LogInformation(
                "POST /sessions/{SessionId}/messages completed. Phase={Phase} RoutedAgent={RoutedAgent} CorrelationId={CorrelationId}",
                sessionId,
                result.Phase,
                result.RoutedAgentId,
                correlationId);

            return Results.Ok(new SessionMessageResponse(
                result.SessionId,
                result.Phase,
                result.RoutedAgentId,
                result.Reply,
                result.PatchApplied));
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                exception,
                "POST /sessions/{SessionId}/messages validation failed. CorrelationId={CorrelationId}",
                sessionId,
                correlationId);
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            logger.LogWarning(
                exception,
                "POST /sessions/{SessionId}/messages returned not found. CorrelationId={CorrelationId}",
                sessionId,
                correlationId);
            return Results.NotFound();
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(
                exception,
                "POST /sessions/{SessionId}/messages rejected for current phase. CorrelationId={CorrelationId}",
                sessionId,
                correlationId);
            return Results.Conflict(new { error = exception.Message });
        }
    }

    private static async Task<IResult> GetTruthMapAsync(
        Guid sessionId,
        SessionService sessionService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(SessionsApiLoggerCategory);
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
    }

    private static async Task<IResult> GetTranscriptAsync(
        Guid sessionId,
        SessionService sessionService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(SessionsApiLoggerCategory);
        var session = await sessionService.GetSessionAsync(sessionId, cancellationToken);

        if (session is null)
        {
            logger.LogWarning("GET /sessions/{SessionId}/transcript returned not found.", sessionId);
            return Results.NotFound();
        }

        var transcript = await sessionService.GetTranscriptAsync(sessionId, cancellationToken);
        logger.LogInformation(
            "GET /sessions/{SessionId}/transcript returned {MessageCount} messages.",
            sessionId,
            transcript.Count);

        return Results.Ok(transcript.Select(message => new TranscriptMessageResponse(
            message.Id,
            message.Type.ToString().ToLowerInvariant(),
            message.AgentId,
            message.Content,
            message.Round,
            message.IsStreaming,
            message.CreatedAtUtc)));
    }

    private static SessionResponse ToResponse(SessionState session)
    {
        return new SessionResponse(
            session.SessionId,
            session.Status.ToString(),
            session.Phase.ToString(),
            session.Mode.ToString(),
            session.FrictionLevel,
            session.RoundNumber,
            session.TargetedLoopCount);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var fromHeader = context.Request.Headers[CorrelationHeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fromHeader))
        {
            context.TraceIdentifier = fromHeader;
            return fromHeader;
        }

        return context.TraceIdentifier;
    }
}
