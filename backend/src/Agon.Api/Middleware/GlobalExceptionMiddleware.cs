using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net;

namespace Agon.Api.Middleware;

/// <summary>
/// Global exception handler that converts unhandled exceptions to RFC 7807 ProblemDetails responses.
/// Includes correlation ID tracking for debugging.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Activity.Current?.Id ?? context.TraceIdentifier;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception occurred. CorrelationId: {CorrelationId}, Path: {Path}",
                correlationId,
                context.Request.Path);

            await HandleExceptionAsync(context, ex, correlationId);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception, string correlationId)
    {
        context.Response.ContentType = "application/problem+json";

        var (statusCode, title) = exception switch
        {
            ArgumentNullException or ArgumentException => 
                (HttpStatusCode.BadRequest, "Invalid request data"),
            InvalidOperationException => 
                (HttpStatusCode.Conflict, "Operation not allowed in current state"),
            UnauthorizedAccessException => 
                (HttpStatusCode.Forbidden, "Access forbidden"),
            KeyNotFoundException => 
                (HttpStatusCode.NotFound, "Resource not found"),
            _ => 
                (HttpStatusCode.InternalServerError, "An unexpected error occurred")
        };

        context.Response.StatusCode = (int)statusCode;

        var problemDetails = new ProblemDetails
        {
            Type = "https://httpstatuses.io/" + (int)statusCode,
            Title = title,
            Status = (int)statusCode,
            Instance = context.Request.Path,
            Detail = _environment.IsDevelopment() 
                ? exception.Message 
                : "An error occurred while processing your request."
        };

        // Add correlation ID for debugging
        problemDetails.Extensions["correlationId"] = correlationId;

        // In development, include stack trace
        if (_environment.IsDevelopment())
        {
            problemDetails.Extensions["exception"] = exception.GetType().Name;
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
        }

        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}
