using Agon.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Text.Json;

namespace Agon.Api.Tests.Middleware;

/// <summary>
/// Unit tests for GlobalExceptionMiddleware.
/// Tests that all exception types are mapped to correct HTTP status codes.
/// </summary>
public class GlobalExceptionMiddlewareTests
{
    private static GlobalExceptionMiddleware CreateMiddleware(
        RequestDelegate next,
        bool isDevelopment = false)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(isDevelopment ? "Development" : "Production");

        return new GlobalExceptionMiddleware(
            next,
            NullLogger<GlobalExceptionMiddleware>.Instance,
            env);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenNoException_PassesThrough()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200); // default status
    }

    // ── Exception type mappings ────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenArgumentNullException_Returns400BadRequest()
    {
        // Arrange
        RequestDelegate next = _ => throw new ArgumentNullException("param");
        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadBodyAsync(context);
        body.Should().Contain("Invalid request data");
    }

    [Fact]
    public async Task InvokeAsync_WhenArgumentException_Returns400BadRequest()
    {
        // Arrange
        RequestDelegate next = _ => throw new ArgumentException("bad argument");
        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_WhenInvalidOperationException_Returns409Conflict()
    {
        // Arrange
        RequestDelegate next = _ => throw new InvalidOperationException("not allowed");
        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(409);
        var body = await ReadBodyAsync(context);
        body.Should().Contain("Operation not allowed");
    }

    [Fact]
    public async Task InvokeAsync_WhenUnauthorizedAccessException_Returns403Forbidden()
    {
        // Arrange
        RequestDelegate next = _ => throw new UnauthorizedAccessException("access denied");
        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(403);
        var body = await ReadBodyAsync(context);
        body.Should().Contain("Access forbidden");
    }

    [Fact]
    public async Task InvokeAsync_WhenKeyNotFoundException_Returns404NotFound()
    {
        // Arrange
        RequestDelegate next = _ => throw new KeyNotFoundException("resource not found");
        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(404);
        var body = await ReadBodyAsync(context);
        body.Should().Contain("Resource not found");
    }

    [Fact]
    public async Task InvokeAsync_WhenUnhandledException_Returns500InternalServerError()
    {
        // Arrange
        RequestDelegate next = _ => throw new NotSupportedException("unsupported operation");
        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(500);
        var body = await ReadBodyAsync(context);
        body.Should().Contain("An unexpected error occurred");
    }

    // ── Development environment ────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_InDevelopment_IncludesExceptionTypeInResponse()
    {
        // Arrange
        RequestDelegate next = _ => throw new NotImplementedException("not yet implemented");
        var middleware = CreateMiddleware(next, isDevelopment: true);
        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(500);
        var body = await ReadBodyAsync(context);
        body.Should().Contain("NotImplementedException");
    }

    [Fact]
    public async Task InvokeAsync_InProduction_DoesNotIncludeExceptionTypeInResponse()
    {
        // Arrange
        RequestDelegate next = _ => throw new NotImplementedException("internal details");
        var middleware = CreateMiddleware(next, isDevelopment: false);
        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(500);
        var body = await ReadBodyAsync(context);
        // In production, exception type should not be exposed
        body.Should().NotContain("NotImplementedException");
    }

    // ── Response format ────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ResponseIsJsonWithProblemDetailsFormat()
    {
        // Arrange
        RequestDelegate next = _ => throw new ArgumentException("bad input");
        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert - response body should be valid JSON with problem details fields
        var body = await ReadBodyAsync(context);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.TryGetProperty("title", out _).Should().BeTrue();
        root.TryGetProperty("status", out _).Should().BeTrue();
        root.TryGetProperty("correlationId", out _).Should().BeTrue("correlation ID should be in extensions");
    }

    [Fact]
    public async Task InvokeAsync_ResponseContainsCorrelationId()
    {
        // Arrange
        RequestDelegate next = _ => throw new InvalidOperationException("error");
        var middleware = CreateMiddleware(next);
        var context = CreateHttpContext();
        context.TraceIdentifier = "test-trace-id-12345";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var body = await ReadBodyAsync(context);
        body.Should().Contain("correlationId");
    }

    // ── Helper ─────────────────────────────────────────────────────────────────

    private static async Task<string> ReadBodyAsync(DefaultHttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
