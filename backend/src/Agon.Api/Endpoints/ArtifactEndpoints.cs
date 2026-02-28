using Agon.Api.Contracts;
using Agon.Application.Interfaces;
using Agon.Application.Services;

namespace Agon.Api.Endpoints;

/// <summary>
/// Endpoint handlers for artifact API routes.
/// </summary>
public static class ArtifactEndpoints
{
    private const string ArtifactsApiLoggerCategory = "ArtifactsApi";
    private const string CorrelationHeaderName = "X-Correlation-ID";

    /// <summary>
    /// Maps all artifact-related endpoints to the application.
    /// </summary>
    public static void MapArtifactEndpoints(this WebApplication app)
    {
        app.MapGet("/sessions/{sessionId:guid}/artifacts", ListArtifactsAsync);
        app.MapGet("/sessions/{sessionId:guid}/artifacts/{type}", GetArtifactAsync);
        app.MapPost("/sessions/{sessionId:guid}/artifacts/export", ExportArtifactsAsync);
    }

    private static async Task<IResult> ListArtifactsAsync(
        Guid sessionId,
        ArtifactService artifactService,
        SessionService sessionService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(ArtifactsApiLoggerCategory);

        var session = await sessionService.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            logger.LogWarning(
                "GET /sessions/{SessionId}/artifacts returned not found.",
                sessionId);
            return Results.NotFound();
        }

        var availableTypes = artifactService.GetAvailableTypes();
        logger.LogInformation(
            "GET /sessions/{SessionId}/artifacts returned {TypeCount} types.",
            sessionId,
            availableTypes.Count);

        return Results.Ok(new ArtifactListResponse(sessionId, availableTypes));
    }

    private static async Task<IResult> GetArtifactAsync(
        Guid sessionId,
        string type,
        ArtifactService artifactService,
        SessionService sessionService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(ArtifactsApiLoggerCategory);

        var session = await sessionService.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            logger.LogWarning(
                "GET /sessions/{SessionId}/artifacts/{Type} returned not found (session).",
                sessionId,
                type);
            return Results.NotFound();
        }

        if (!ArtifactService.TryParseArtifactType(type, out var artifactType))
        {
            logger.LogWarning(
                "GET /sessions/{SessionId}/artifacts/{Type} rejected due to invalid type.",
                sessionId,
                type);
            return Results.BadRequest(new { error = $"Invalid artifact type '{type}'." });
        }

        var result = await artifactService.GenerateArtifactAsync(sessionId, artifactType, cancellationToken);
        if (result is null)
        {
            logger.LogWarning(
                "GET /sessions/{SessionId}/artifacts/{Type} returned not found (truth map).",
                sessionId,
                type);
            return Results.NotFound();
        }

        logger.LogInformation(
            "GET /sessions/{SessionId}/artifacts/{Type} generated successfully.",
            sessionId,
            type);

        return Results.Ok(new ArtifactResponse(
            result.SessionId,
            result.Type,
            result.Content,
            result.GeneratedAtUtc));
    }

    private static async Task<IResult> ExportArtifactsAsync(
        Guid sessionId,
        ExportArtifactsRequest? request,
        ArtifactService artifactService,
        SessionService sessionService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(httpContext);
        httpContext.Response.Headers[CorrelationHeaderName] = correlationId;

        var logger = loggerFactory.CreateLogger(ArtifactsApiLoggerCategory);
        logger.LogInformation(
            "POST /sessions/{SessionId}/artifacts/export received. CorrelationId={CorrelationId}",
            sessionId,
            correlationId);

        var session = await sessionService.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            logger.LogWarning(
                "POST /sessions/{SessionId}/artifacts/export returned not found. CorrelationId={CorrelationId}",
                sessionId,
                correlationId);
            return Results.NotFound();
        }

        List<ArtifactType>? typesToExport = null;
        if (request?.Types is { Count: > 0 })
        {
            typesToExport = new List<ArtifactType>();
            foreach (var typeName in request.Types)
            {
                if (!ArtifactService.TryParseArtifactType(typeName, out var artifactType))
                {
                    logger.LogWarning(
                        "POST /sessions/{SessionId}/artifacts/export rejected due to invalid type '{Type}'. CorrelationId={CorrelationId}",
                        sessionId,
                        typeName,
                        correlationId);
                    return Results.BadRequest(new { error = $"Invalid artifact type '{typeName}'." });
                }
                typesToExport.Add(artifactType);
            }
        }

        var zipBytes = await artifactService.ExportArtifactsAsync(sessionId, typesToExport, cancellationToken);
        if (zipBytes is null)
        {
            logger.LogWarning(
                "POST /sessions/{SessionId}/artifacts/export returned not found (truth map). CorrelationId={CorrelationId}",
                sessionId,
                correlationId);
            return Results.NotFound();
        }

        var fileName = $"agon-artifacts-{sessionId:N}.zip";
        logger.LogInformation(
            "POST /sessions/{SessionId}/artifacts/export completed. ZipSize={ZipSize} CorrelationId={CorrelationId}",
            sessionId,
            zipBytes.Length,
            correlationId);

        return Results.File(
            zipBytes,
            contentType: "application/zip",
            fileDownloadName: fileName);
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
