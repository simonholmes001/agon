using System.Text.Json;
using Agon.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agon.Api.Tests.Middleware;

public class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenNoException_CallsNextDelegate()
    {
        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var context = new DefaultHttpContext();
        var middleware = new GlobalExceptionMiddleware(next, NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Headers.Should().NotContainKey("X-Correlation-ID");
    }

    [Fact]
    public async Task InvokeAsync_WhenUnhandledException_RespondsWithProblemDetailsAndTraceCorrelationId()
    {
        RequestDelegate next = _ => throw new InvalidOperationException("boom");
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-123";
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/sessions/test";
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionMiddleware(next, NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.Should().Be("application/problem+json");
        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be("trace-123");

        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        body.RootElement.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status500InternalServerError);
        body.RootElement.GetProperty("correlationId").GetString().Should().Be("trace-123");
    }

    [Fact]
    public async Task InvokeAsync_WhenRequestCarriesCorrelationHeader_UsesItInResponseAndBody()
    {
        RequestDelegate next = _ => throw new InvalidOperationException("boom");
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-fallback";
        context.Request.Headers["X-Correlation-ID"] = "incoming-456";
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionMiddleware(next, NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be("incoming-456");

        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        body.RootElement.GetProperty("correlationId").GetString().Should().Be("incoming-456");
    }
}
