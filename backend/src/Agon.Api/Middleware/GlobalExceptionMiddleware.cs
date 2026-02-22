using Microsoft.AspNetCore.Mvc;

namespace Agon.Api.Middleware;

public class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private const string CorrelationHeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            var correlationId = ResolveCorrelationId(context);
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            context.Response.Headers[CorrelationHeaderName] = correlationId;

            logger.LogError(
                exception,
                "Unhandled exception. CorrelationId={CorrelationId} Method={Method} Path={Path}",
                correlationId,
                context.Request.Method,
                context.Request.Path);

            var problemDetails = new ProblemDetails
            {
                Type = "https://httpstatuses.com/500",
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred. Contact support with the correlation ID.",
                Instance = context.Request.Path
            };
            problemDetails.Extensions["correlationId"] = correlationId;

            await context.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);
        }
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
