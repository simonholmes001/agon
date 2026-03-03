using Microsoft.AspNetCore.Mvc;
using Serilog.Context;

namespace Agon.Api.Middleware;

public class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private const string CorrelationHeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        // Push the correlation ID into every log event emitted during this request.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            try
            {
                await next(context);
            }
            catch (OperationCanceledException ex) when (context.RequestAborted.IsCancellationRequested)
            {
                // Client disconnected — not an error worth logging at Error level.
                logger.LogWarning(
                    ex,
                    "Request cancelled by client. CorrelationId={CorrelationId} Method={Method} Path={Path}",
                    correlationId,
                    context.Request.Method,
                    context.Request.Path);
            }
            catch (Exception exception)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";
                context.Response.Headers[CorrelationHeaderName] = correlationId;

                logger.LogError(
                    exception,
                    "Unhandled exception. CorrelationId={CorrelationId} Method={Method} Path={Path} " +
                    "ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage}",
                    correlationId,
                    context.Request.Method,
                    context.Request.Path,
                    exception.GetType().Name,
                    exception.Message);

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
