using Agon.Api.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Agon.Api.Tests.Middleware;

public sealed class GlobalExceptionMiddlewareTests
{
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;
    private readonly DefaultHttpContext _httpContext;
    private bool _nextCalled;

    public GlobalExceptionMiddlewareTests()
    {
        _logger = Substitute.For<ILogger<GlobalExceptionMiddleware>>();
        _environment = Substitute.For<IHostEnvironment>();
        _httpContext = new DefaultHttpContext();
        _httpContext.Response.Body = new MemoryStream();
        _nextCalled = false;
    }

    [Fact]
    public async Task InvokeAsync_WhenNoExceptionThrown_ShouldCallNext()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var middleware = new GlobalExceptionMiddleware(
            next: _ => { _nextCalled = true; return Task.CompletedTask; },
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        Assert.True(_nextCalled);
        Assert.Equal(200, _httpContext.Response.StatusCode); // Default status
    }

    [Fact]
    public async Task InvokeAsync_WhenArgumentNullExceptionThrown_ShouldReturn400BadRequest()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        const string paramName = "testParam";
        var exception = new ArgumentNullException(paramName, "Parameter cannot be null");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        Assert.Equal((int)HttpStatusCode.BadRequest, _httpContext.Response.StatusCode);
        Assert.StartsWith("application/", _httpContext.Response.ContentType);
        Assert.Contains("json", _httpContext.Response.ContentType);
        
        var problemDetails = await DeserializeProblemDetails();
        Assert.Equal("Invalid request data", problemDetails!.Title);
        Assert.Equal((int)HttpStatusCode.BadRequest, problemDetails.Status);
    }

    [Fact]
    public async Task InvokeAsync_WhenArgumentExceptionThrown_ShouldReturn400BadRequest()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new ArgumentException("Invalid argument value");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        Assert.Equal((int)HttpStatusCode.BadRequest, _httpContext.Response.StatusCode);
        
        var problemDetails = await DeserializeProblemDetails();
        Assert.Equal("Invalid request data", problemDetails!.Title);
    }

    [Fact]
    public async Task InvokeAsync_WhenInvalidOperationExceptionThrown_ShouldReturn409Conflict()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new InvalidOperationException("Operation not allowed");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        Assert.Equal((int)HttpStatusCode.Conflict, _httpContext.Response.StatusCode);
        
        var problemDetails = await DeserializeProblemDetails();
        Assert.Equal("Operation not allowed in current state", problemDetails!.Title);
        Assert.Equal((int)HttpStatusCode.Conflict, problemDetails.Status);
    }

    [Fact]
    public async Task InvokeAsync_WhenUnauthorizedAccessExceptionThrown_ShouldReturn403Forbidden()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new UnauthorizedAccessException("Access denied");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        Assert.Equal((int)HttpStatusCode.Forbidden, _httpContext.Response.StatusCode);
        
        var problemDetails = await DeserializeProblemDetails();
        Assert.Equal("Access forbidden", problemDetails!.Title);
        Assert.Equal((int)HttpStatusCode.Forbidden, problemDetails.Status);
    }

    [Fact]
    public async Task InvokeAsync_WhenKeyNotFoundExceptionThrown_ShouldReturn404NotFound()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new KeyNotFoundException("Resource not found");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        Assert.Equal((int)HttpStatusCode.NotFound, _httpContext.Response.StatusCode);
        
        var problemDetails = await DeserializeProblemDetails();
        Assert.Equal("Resource not found", problemDetails!.Title);
        Assert.Equal((int)HttpStatusCode.NotFound, problemDetails.Status);
    }

    [Fact]
    public async Task InvokeAsync_WhenGenericExceptionThrown_ShouldReturn500InternalServerError()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new Exception("Unexpected error");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        Assert.Equal((int)HttpStatusCode.InternalServerError, _httpContext.Response.StatusCode);
        
        var problemDetails = await DeserializeProblemDetails();
        Assert.Equal("An unexpected error occurred", problemDetails!.Title);
        Assert.Equal((int)HttpStatusCode.InternalServerError, problemDetails.Status);
    }

    [Fact]
    public async Task InvokeAsync_InProductionEnvironment_ShouldNotExposeExceptionDetails()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new Exception("Internal system error");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        var problemDetails = await DeserializeProblemDetails();
        Assert.Equal("An error occurred while processing your request.", problemDetails!.Detail);
        Assert.False(problemDetails.Extensions.ContainsKey("exception"));
        Assert.False(problemDetails.Extensions.ContainsKey("stackTrace"));
    }

    [Fact]
    public async Task InvokeAsync_InDevelopmentEnvironment_ShouldExposeExceptionDetails()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Development);
        var exception = new InvalidOperationException("Detailed error message");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        var problemDetails = await DeserializeProblemDetails();
        Assert.Equal("Detailed error message", problemDetails!.Detail);
        Assert.True(problemDetails.Extensions.ContainsKey("exception"));
        Assert.Equal("InvalidOperationException", problemDetails.Extensions["exception"]!.ToString());
        Assert.True(problemDetails.Extensions.ContainsKey("stackTrace"));
    }

    [Fact]
    public async Task InvokeAsync_ShouldIncludeCorrelationIdInResponse()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new Exception("Test exception");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        var problemDetails = await DeserializeProblemDetails();
        Assert.True(problemDetails!.Extensions.ContainsKey("correlationId"));
        Assert.NotNull(problemDetails.Extensions["correlationId"]);
        Assert.NotEmpty(problemDetails.Extensions["correlationId"]!.ToString()!);
    }

    [Fact]
    public async Task InvokeAsync_WhenActivityCurrentExists_ShouldUseActivityIdAsCorrelationId()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new Exception("Test exception");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        var activity = new Activity("TestOperation");
        activity.Start();
        var expectedCorrelationId = activity.Id!;

        try
        {
            // Act
            await middleware.InvokeAsync(_httpContext);

            // Assert
            var problemDetails = await DeserializeProblemDetails();
            Assert.Equal(expectedCorrelationId, problemDetails!.Extensions["correlationId"]!.ToString());
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public async Task InvokeAsync_WhenActivityCurrentNull_ShouldUseTraceIdentifierAsCorrelationId()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new Exception("Test exception");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        var expectedTraceId = "test-trace-id-12345";
        _httpContext.TraceIdentifier = expectedTraceId;

        // Ensure no Activity.Current
        var currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            currentActivity.Stop();
        }

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        var problemDetails = await DeserializeProblemDetails();
        Assert.Equal(expectedTraceId, problemDetails!.Extensions["correlationId"]!.ToString());
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetCorrectProblemDetailsType()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new KeyNotFoundException("Not found");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        var problemDetails = await DeserializeProblemDetails();
        Assert.Equal("https://httpstatuses.io/404", problemDetails!.Type);
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetCorrectProblemDetailsInstance()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new Exception("Test exception");
        _httpContext.Request.Path = "/api/sessions/test-id";
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        var problemDetails = await DeserializeProblemDetails();
        Assert.Equal("/api/sessions/test-id", problemDetails!.Instance);
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionThrown_ShouldLogError()
    {
        // Arrange
        _environment.EnvironmentName.Returns(Environments.Production);
        var exception = new Exception("Test exception");
        var middleware = new GlobalExceptionMiddleware(
            next: _ => throw exception,
            _logger,
            _environment);

        // Act
        await middleware.InvokeAsync(_httpContext);

        // Assert
        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Unhandled exception occurred")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    private async Task<ProblemDetails?> DeserializeProblemDetails()
    {
        _httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(_httpContext.Response.Body).ReadToEndAsync();
        
        return JsonSerializer.Deserialize<ProblemDetails>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}
