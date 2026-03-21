using Agon.Api.Configuration;
using Agon.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Agon.Api.Tests.Middleware;

/// <summary>
/// Unit tests for MinimumCliVersionMiddleware.
/// Tests all version checking and bypass scenarios.
/// </summary>
public class MinimumCliVersionMiddlewareTests
{
    private const string CliVersionHeader = "X-Agon-CLI-Version";

    private static MinimumCliVersionMiddleware CreateMiddleware(
        RequestDelegate next,
        string? minCliVersion = "1.0.0",
        string environmentName = "Production")
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var config = new AgonConfiguration
        {
            MinCliVersion = minCliVersion
        };

        return new MinimumCliVersionMiddleware(
            next,
            NullLogger<MinimumCliVersionMiddleware>.Instance,
            env,
            config);
    }

    private static DefaultHttpContext CreateHttpContext(
        string path = "/sessions",
        string? cliVersion = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = path;

        if (cliVersion is not null)
        {
            context.Request.Headers[CliVersionHeader] = cliVersion;
        }

        return context;
    }

    // ── Bypass scenarios ───────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_InTestingEnvironment_BypassesVersionCheck()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "1.0.0", environmentName: "Testing");
        var context = CreateHttpContext(cliVersion: null); // No version header

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_WhenPathIsHealth_BypassesVersionCheck()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "1.0.0");
        var context = CreateHttpContext(path: "/health", cliVersion: null);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_WhenPathStartsWithHubs_BypassesVersionCheck()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "1.0.0");
        var context = CreateHttpContext(path: "/hubs/debate", cliVersion: null);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenPathStartsWithOpenapi_BypassesVersionCheck()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "1.0.0");
        var context = CreateHttpContext(path: "/openapi/v1.json", cliVersion: null);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenPathStartsWithSwagger_BypassesVersionCheck()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "1.0.0");
        var context = CreateHttpContext(path: "/swagger/index.html", cliVersion: null);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenMinCliVersionIsNull_BypassesVersionCheck()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: null);
        var context = CreateHttpContext(cliVersion: null);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenMinCliVersionIsEmpty_BypassesVersionCheck()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "   ");
        var context = CreateHttpContext(cliVersion: null);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
    }

    // ── Version enforcement ────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenClientVersionMeetsMinimum_PassesThrough()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "1.0.0");
        var context = CreateHttpContext(cliVersion: "1.0.0");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_WhenClientVersionIsHigher_PassesThrough()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "1.0.0");
        var context = CreateHttpContext(cliVersion: "2.5.3");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenClientVersionBelowMinimum_Returns426UpgradeRequired()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "2.0.0");
        var context = CreateHttpContext(cliVersion: "1.9.9");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeFalse();
        context.Response.StatusCode.Should().Be(426);
    }

    [Fact]
    public async Task InvokeAsync_WhenCliVersionHeaderMissing_Returns426UpgradeRequired()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "1.0.0");
        var context = CreateHttpContext(cliVersion: null); // No version header

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeFalse();
        context.Response.StatusCode.Should().Be(426);
    }

    [Fact]
    public async Task InvokeAsync_WhenCliVersionHeaderIsInvalidFormat_Returns426UpgradeRequired()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "1.0.0");
        var context = CreateHttpContext(cliVersion: "not-a-semver");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeFalse();
        context.Response.StatusCode.Should().Be(426);
    }

    [Fact]
    public async Task InvokeAsync_When426Returned_ResponseContainsRequiredVersion()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next, minCliVersion: "3.2.1");
        var context = CreateHttpContext(cliVersion: "1.0.0");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(426);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        body.Should().Contain("3.2.1");
        body.Should().Contain("installCommand");
    }

    [Fact]
    public async Task InvokeAsync_WhenMinCliVersionIsInvalidSemver_BypassesEnforcement()
    {
        // Arrange - Invalid MinCliVersion config should not crash the app; instead bypass enforcement
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "not-valid-semver");
        var context = CreateHttpContext(cliVersion: null);

        // Act
        await middleware.InvokeAsync(context);

        // Assert - Should bypass when MinCliVersion itself is invalid
        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ClientVersionWithVPrefix_IsAccepted()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "1.0.0");
        var context = CreateHttpContext(cliVersion: "v1.5.0"); // v-prefixed

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ClientVersionWithPrereleaseLabel_IsAccepted_WhenVersionMeetsMinimum()
    {
        // Arrange
        var called = false;
        RequestDelegate next = ctx =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next, minCliVersion: "1.0.0");
        var context = CreateHttpContext(cliVersion: "2.0.0-beta.1"); // prerelease

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        called.Should().BeTrue();
    }
}
