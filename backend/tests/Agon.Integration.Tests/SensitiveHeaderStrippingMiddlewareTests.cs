using Agon.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Agon.Integration.Tests;

/// <summary>
/// Unit tests for SensitiveHeaderStrippingMiddleware.
/// Verifies that provider key headers are stripped before any handler sees them.
/// </summary>
public sealed class SensitiveHeaderStrippingMiddlewareTests
{
    private readonly ILogger<SensitiveHeaderStrippingMiddleware> _logger;

    public SensitiveHeaderStrippingMiddlewareTests()
    {
        _logger = Substitute.For<ILogger<SensitiveHeaderStrippingMiddleware>>();
    }

    [Fact]
    public async Task InvokeAsync_StripsProviderKeyHeaders_BeforeNextMiddleware()
    {
        // Arrange
        var capturedHeaders = new Dictionary<string, string>();
        var next = new RequestDelegate(ctx =>
        {
            foreach (var (key, value) in ctx.Request.Headers)
                capturedHeaders[key] = value.ToString();
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Agon-Provider-Key-openai"] = "sk-secret";
        context.Request.Headers["X-Agon-Provider-Key-anthropic"] = "ant-secret";
        context.Request.Headers["Authorization"] = "Bearer token-not-stripped";

        var sut = new SensitiveHeaderStrippingMiddleware(next, _logger);

        // Act
        await sut.InvokeAsync(context);

        // Assert — provider key headers must be absent when the next middleware sees the request
        capturedHeaders.Keys.Should().NotContain(
            k => k.StartsWith("X-Agon-Provider-Key-", StringComparison.OrdinalIgnoreCase),
            "provider key headers must be stripped before reaching any handler");

        // Non-sensitive headers must be preserved
        capturedHeaders.Should().ContainKey("Authorization",
            "unrelated headers must not be stripped");
    }

    [Fact]
    public async Task InvokeAsync_CaseInsensitive_StripsAllCasings()
    {
        // Arrange
        var capturedHeaderNames = new List<string>();
        var next = new RequestDelegate(ctx =>
        {
            capturedHeaderNames.AddRange(ctx.Request.Headers.Keys);
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Headers["x-agon-provider-key-openai"] = "lowercase";
        context.Request.Headers["X-AGON-PROVIDER-KEY-ANTHROPIC"] = "uppercase";

        var sut = new SensitiveHeaderStrippingMiddleware(next, _logger);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        capturedHeaderNames.Should().NotContain(
            k => k.StartsWith("x-agon-provider-key-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InvokeAsync_WithNoSensitiveHeaders_CallsNextWithoutModification()
    {
        // Arrange
        var nextCalled = false;
        var next = new RequestDelegate(ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Normal-Header"] = "normal-value";

        var sut = new SensitiveHeaderStrippingMiddleware(next, _logger);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue("next middleware must always be called");
        context.Request.Headers.Should().ContainKey("X-Normal-Header",
            "non-sensitive headers must not be stripped");
    }

    [Fact]
    public void StripSensitiveHeaders_ReturnsCountOfStrippedHeaders()
    {
        // Arrange
        var headers = new HeaderDictionary
        {
            ["X-Agon-Provider-Key-openai"] = "sk-secret",
            ["X-Agon-Provider-Key-anthropic"] = "ant-secret",
            ["Authorization"] = "Bearer some-token",
            ["Content-Type"] = "application/json"
        };

        // Act
        var stripped = SensitiveHeaderStrippingMiddleware.StripSensitiveHeaders(headers);

        // Assert
        stripped.Should().Be(2, "two provider key headers should be stripped");
        headers.Should().NotContainKey("X-Agon-Provider-Key-openai");
        headers.Should().NotContainKey("X-Agon-Provider-Key-anthropic");
        headers.Should().ContainKey("Authorization", "non-sensitive headers must survive");
        headers.Should().ContainKey("Content-Type", "non-sensitive headers must survive");
    }

    [Fact]
    public void StripSensitiveHeaders_WithNoProviderKeyHeaders_ReturnsZero()
    {
        // Arrange
        var headers = new HeaderDictionary
        {
            ["Authorization"] = "Bearer token",
            ["Content-Type"] = "application/json"
        };

        // Act
        var stripped = SensitiveHeaderStrippingMiddleware.StripSensitiveHeaders(headers);

        // Assert
        stripped.Should().Be(0, "no headers should be stripped when none match the provider key prefix");
    }

    [Fact]
    public void StripSensitiveHeaders_WithEmptyHeaders_ReturnsZero()
    {
        // Arrange
        var headers = new HeaderDictionary();

        // Act
        var stripped = SensitiveHeaderStrippingMiddleware.StripSensitiveHeaders(headers);

        // Assert
        stripped.Should().Be(0);
    }
}
