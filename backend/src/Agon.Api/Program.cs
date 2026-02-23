using Agon.Api.Configuration;
using Agon.Api.Middleware;
using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Services;
using Agon.Domain.Agents;
using Agon.Domain.Sessions;
using Agon.Infrastructure.Agents;
using Agon.Infrastructure.Persistence.InMemory;
using Agon.Infrastructure.SignalR;
using Microsoft.AspNetCore.SignalR;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);
var isTestingEnvironment =
    builder.Environment.IsEnvironment("Test")
    || builder.Environment.IsEnvironment("Testing");

const string FrontendCorsPolicy = "FrontendCorsPolicy";
const string CorrelationHeaderName = "X-Correlation-ID";
const string OpenAiDefaultModel = "gpt-5.2";
const string TechnicalArchitectTemporaryModelDefault = "gpt-5.2";
const string GeminiDefaultModel = "gemini-3.1-pro-preview";
const string AnthropicDefaultModel = "claude-opus-4-6";
const int DefaultMaxOutputTokens = 2400;
const int DefaultModeratorMaxOutputTokens = 3200;
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:3000", "https://localhost:3000"];
var dotEnvLoadResult = isTestingEnvironment
    ? new DotEnvLoadResult(null, 0, 0)
    : DotEnvLoader.Load(builder.Environment.ContentRootPath);

static (double? Value, bool IsInvalid) ParseTemperatureSetting(string? rawValue)
{
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        return (null, false);
    }

    var parsed = double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature)
        && temperature is >= 0 and <= 2;
    return parsed
        ? (temperature, false)
        : (null, true);
}

static (int? Value, bool IsInvalid) ParseMaxOutputTokensSetting(string? rawValue)
{
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        return (null, false);
    }

    var parsed = int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokens)
        && tokens is >= 128 and <= 16384;
    return parsed
        ? (tokens, false)
        : (null, true);
}

var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_KEY")
    ?? builder.Configuration["OPENAI_KEY"]
    ?? builder.Configuration["OpenAI:ApiKey"];
var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL")
    ?? builder.Configuration["OPENAI_MODEL"]
    ?? builder.Configuration["OpenAI:Model"]
    ?? OpenAiDefaultModel;
var openAiTemperatureRaw = Environment.GetEnvironmentVariable("OPENAI_TEMPERATURE")
    ?? builder.Configuration["OPENAI_TEMPERATURE"]
    ?? builder.Configuration["OpenAI:Temperature"];
var openAiTemperatureSetting = ParseTemperatureSetting(openAiTemperatureRaw);
var openAiTemperature = openAiTemperatureSetting.Value;
var openAiMaxOutputTokensRaw = Environment.GetEnvironmentVariable("OPENAI_MAX_OUTPUT_TOKENS")
    ?? builder.Configuration["OPENAI_MAX_OUTPUT_TOKENS"]
    ?? builder.Configuration["OpenAI:MaxOutputTokens"];
var openAiMaxOutputTokensSetting = ParseMaxOutputTokensSetting(openAiMaxOutputTokensRaw);
var openAiMaxOutputTokens = openAiMaxOutputTokensSetting.Value ?? DefaultMaxOutputTokens;
var geminiApiKey = Environment.GetEnvironmentVariable("GEMINI_KEY")
    ?? builder.Configuration["GEMINI_KEY"]
    ?? builder.Configuration["Gemini:ApiKey"];
var geminiModel = Environment.GetEnvironmentVariable("GEMINI_MODEL")
    ?? builder.Configuration["GEMINI_MODEL"]
    ?? builder.Configuration["Gemini:Model"]
    ?? GeminiDefaultModel;
var geminiTemperatureRaw = Environment.GetEnvironmentVariable("GEMINI_TEMPERATURE")
    ?? builder.Configuration["GEMINI_TEMPERATURE"]
    ?? builder.Configuration["Gemini:Temperature"];
var geminiTemperatureSetting = ParseTemperatureSetting(geminiTemperatureRaw);
var geminiTemperature = geminiTemperatureSetting.Value;
var geminiMaxOutputTokensRaw = Environment.GetEnvironmentVariable("GEMINI_MAX_OUTPUT_TOKENS")
    ?? builder.Configuration["GEMINI_MAX_OUTPUT_TOKENS"]
    ?? builder.Configuration["Gemini:MaxOutputTokens"];
var geminiMaxOutputTokensSetting = ParseMaxOutputTokensSetting(geminiMaxOutputTokensRaw);
var geminiMaxOutputTokens = geminiMaxOutputTokensSetting.Value ?? DefaultMaxOutputTokens;
var anthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_KEY")
    ?? Environment.GetEnvironmentVariable("CLAUDE_KEY")
    ?? builder.Configuration["ANTHROPIC_KEY"]
    ?? builder.Configuration["CLAUDE_KEY"]
    ?? builder.Configuration["Anthropic:ApiKey"];
var anthropicModel = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")
    ?? builder.Configuration["ANTHROPIC_MODEL"]
    ?? builder.Configuration["Anthropic:Model"]
    ?? AnthropicDefaultModel;
var anthropicTemperatureRaw = Environment.GetEnvironmentVariable("ANTHROPIC_TEMPERATURE")
    ?? builder.Configuration["ANTHROPIC_TEMPERATURE"]
    ?? builder.Configuration["Anthropic:Temperature"];
var anthropicTemperatureSetting = ParseTemperatureSetting(anthropicTemperatureRaw);
var anthropicTemperature = anthropicTemperatureSetting.Value;
var anthropicMaxOutputTokensRaw = Environment.GetEnvironmentVariable("ANTHROPIC_MAX_OUTPUT_TOKENS")
    ?? builder.Configuration["ANTHROPIC_MAX_OUTPUT_TOKENS"]
    ?? builder.Configuration["Anthropic:MaxOutputTokens"];
var anthropicMaxOutputTokensSetting = ParseMaxOutputTokensSetting(anthropicMaxOutputTokensRaw);
var anthropicMaxOutputTokens = anthropicMaxOutputTokensSetting.Value ?? DefaultMaxOutputTokens;
var technicalArchitectModel = Environment.GetEnvironmentVariable("TECHNICAL_ARCHITECT_MODEL")
    ?? TechnicalArchitectTemporaryModelDefault;
var technicalArchitectTemperatureRaw = Environment.GetEnvironmentVariable("TECHNICAL_ARCHITECT_TEMPERATURE")
    ?? builder.Configuration["TECHNICAL_ARCHITECT_TEMPERATURE"]
    ?? builder.Configuration["TechnicalArchitect:Temperature"];
var technicalArchitectTemperatureSetting = ParseTemperatureSetting(technicalArchitectTemperatureRaw);
var technicalArchitectTemperature = technicalArchitectTemperatureSetting.Value ?? openAiTemperature;
var technicalArchitectMaxOutputTokensRaw = Environment.GetEnvironmentVariable("TECHNICAL_ARCHITECT_MAX_OUTPUT_TOKENS")
    ?? builder.Configuration["TECHNICAL_ARCHITECT_MAX_OUTPUT_TOKENS"]
    ?? builder.Configuration["TechnicalArchitect:MaxOutputTokens"];
var technicalArchitectMaxOutputTokensSetting = ParseMaxOutputTokensSetting(technicalArchitectMaxOutputTokensRaw);
var technicalArchitectMaxOutputTokens = technicalArchitectMaxOutputTokensSetting.Value ?? openAiMaxOutputTokens;
var synthesisMaxOutputTokensRaw = Environment.GetEnvironmentVariable("SYNTHESIS_MAX_OUTPUT_TOKENS")
    ?? builder.Configuration["SYNTHESIS_MAX_OUTPUT_TOKENS"]
    ?? builder.Configuration["Synthesis:MaxOutputTokens"];
var synthesisMaxOutputTokensSetting = ParseMaxOutputTokensSetting(synthesisMaxOutputTokensRaw);
var synthesisMaxOutputTokens = synthesisMaxOutputTokensSetting.Value
    ?? Math.Max(openAiMaxOutputTokens, DefaultModeratorMaxOutputTokens);

builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Cors.Infrastructure.CorsService", LogLevel.Warning);

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, cors =>
    {
        cors
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSignalR();
builder.Services.AddSingleton<ISessionRepository, InMemorySessionRepository>();
builder.Services.AddSingleton<ITruthMapRepository, InMemoryTruthMapRepository>();
builder.Services.AddSingleton<ITranscriptRepository, InMemoryTranscriptRepository>();
builder.Services.AddSingleton<IEventBroadcaster, SignalREventBroadcaster>();
builder.Services.AddSingleton<Orchestrator>();
builder.Services.AddSingleton<AgentRunner>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddHttpClient();

builder.Services.AddSingleton<ICouncilAgent>(sp =>
{
    if (string.IsNullOrWhiteSpace(openAiApiKey))
    {
        return new ConfigurationErrorCouncilAgent(
            AgentId.ResearchLibrarian,
            modelProvider: "openai",
            errorMessage: "Missing OPENAI_KEY for agent 'research_librarian'.",
            logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
    }

    var logger = sp.GetRequiredService<ILogger<OpenAiCouncilAgent>>();
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var options = new OpenAiCouncilAgentOptions(
        AgentId.ResearchLibrarian,
        ApiKey: openAiApiKey,
        ModelName: openAiModel,
        MaxOutputTokens: openAiMaxOutputTokens,
        Temperature: openAiTemperature);

    return new OpenAiCouncilAgent(httpClientFactory.CreateClient(), options, logger);
});
builder.Services.AddSingleton<ICouncilAgent>(sp =>
{
    if (string.IsNullOrWhiteSpace(geminiApiKey))
    {
        return new ConfigurationErrorCouncilAgent(
            AgentId.FramingChallenger,
            modelProvider: "gemini",
            errorMessage: "Missing GEMINI_KEY for agent 'framing_challenger'.",
            logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
    }

    return new GeminiCouncilAgent(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        new GeminiCouncilAgentOptions(
            AgentId.FramingChallenger,
            geminiApiKey,
            geminiModel,
            MaxOutputTokens: geminiMaxOutputTokens,
            Temperature: geminiTemperature),
        sp.GetRequiredService<ILogger<GeminiCouncilAgent>>());
});
builder.Services.AddSingleton<ICouncilAgent>(sp =>
{
    if (string.IsNullOrWhiteSpace(geminiApiKey))
    {
        return new ConfigurationErrorCouncilAgent(
            AgentId.Contrarian,
            modelProvider: "gemini",
            errorMessage: "Missing GEMINI_KEY for agent 'contrarian'.",
            logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
    }

    return new GeminiCouncilAgent(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        new GeminiCouncilAgentOptions(
            AgentId.Contrarian,
            geminiApiKey,
            geminiModel,
            MaxOutputTokens: geminiMaxOutputTokens,
            Temperature: geminiTemperature),
        sp.GetRequiredService<ILogger<GeminiCouncilAgent>>());
});
builder.Services.AddSingleton<ICouncilAgent>(sp =>
{
    if (string.IsNullOrWhiteSpace(anthropicApiKey))
    {
        return new ConfigurationErrorCouncilAgent(
            AgentId.ProductStrategist,
            modelProvider: "anthropic",
            errorMessage: "Missing ANTHROPIC_KEY or CLAUDE_KEY for agent 'product_strategist'.",
            logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
    }

    return new AnthropicCouncilAgent(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        new AnthropicCouncilAgentOptions(
            AgentId.ProductStrategist,
            anthropicApiKey,
            anthropicModel,
            MaxOutputTokens: anthropicMaxOutputTokens,
            Temperature: anthropicTemperature),
        sp.GetRequiredService<ILogger<AnthropicCouncilAgent>>());
});
builder.Services.AddSingleton<ICouncilAgent>(sp =>
{
    if (string.IsNullOrWhiteSpace(openAiApiKey))
    {
        return new ConfigurationErrorCouncilAgent(
            AgentId.TechnicalArchitect,
            modelProvider: "openai",
            errorMessage: "Missing OPENAI_KEY for agent 'technical_architect' temporary OpenAI override.",
            logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
    }

    return new OpenAiCouncilAgent(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        new OpenAiCouncilAgentOptions(
            AgentId.TechnicalArchitect,
            openAiApiKey,
            technicalArchitectModel,
            MaxOutputTokens: technicalArchitectMaxOutputTokens,
            Temperature: technicalArchitectTemperature),
        sp.GetRequiredService<ILogger<OpenAiCouncilAgent>>());
});
builder.Services.AddSingleton<ICouncilAgent>(sp =>
{
    if (string.IsNullOrWhiteSpace(openAiApiKey))
    {
        return new ConfigurationErrorCouncilAgent(
            AgentId.SocraticClarifier,
            modelProvider: "openai",
            errorMessage: "Missing OPENAI_KEY for agent 'socratic_clarifier'.",
            logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
    }

    return new OpenAiCouncilAgent(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        new OpenAiCouncilAgentOptions(
            AgentId.SocraticClarifier,
            openAiApiKey,
            openAiModel,
            MaxOutputTokens: openAiMaxOutputTokens,
            Temperature: openAiTemperature),
        sp.GetRequiredService<ILogger<OpenAiCouncilAgent>>());
});
builder.Services.AddSingleton<ICouncilAgent>(sp =>
{
    if (string.IsNullOrWhiteSpace(openAiApiKey))
    {
        return new ConfigurationErrorCouncilAgent(
            AgentId.SynthesisValidation,
            modelProvider: "openai",
            errorMessage: "Missing OPENAI_KEY for agent 'synthesis_validation'.",
            logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
    }

    return new OpenAiCouncilAgent(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        new OpenAiCouncilAgentOptions(
            AgentId.SynthesisValidation,
            openAiApiKey,
            openAiModel,
            MaxOutputTokens: synthesisMaxOutputTokens,
            Temperature: openAiTemperature),
        sp.GetRequiredService<ILogger<OpenAiCouncilAgent>>());
});

var app = builder.Build();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors(FrontendCorsPolicy);

if (dotEnvLoadResult.FilePath is not null)
{
    app.Logger.LogInformation(
        ".env configuration loaded. Path={Path} LoadedKeys={LoadedKeys} SkippedExistingKeys={SkippedExistingKeys}",
        dotEnvLoadResult.FilePath,
        dotEnvLoadResult.LoadedCount,
        dotEnvLoadResult.SkippedExistingCount);
}

app.Logger.LogInformation(
    "Council provider configuration. OpenAI={OpenAiConfigured} Gemini={GeminiConfigured} Anthropic={AnthropicConfigured} TechnicalArchitectProvider={TechnicalArchitectProvider} TechnicalArchitectModel={TechnicalArchitectModel}",
    !string.IsNullOrWhiteSpace(openAiApiKey),
    !string.IsNullOrWhiteSpace(geminiApiKey),
    !string.IsNullOrWhiteSpace(anthropicApiKey),
    "openai-temporary-override",
    technicalArchitectModel);

app.Logger.LogInformation(
    "Provider temperature configuration. OpenAI={OpenAiTemperature} Gemini={GeminiTemperature} Anthropic={AnthropicTemperature} TechnicalArchitect={TechnicalArchitectTemperature}",
    openAiTemperature?.ToString(CultureInfo.InvariantCulture) ?? "provider-default",
    geminiTemperature?.ToString(CultureInfo.InvariantCulture) ?? "provider-default",
    anthropicTemperature?.ToString(CultureInfo.InvariantCulture) ?? "provider-default",
    technicalArchitectTemperature?.ToString(CultureInfo.InvariantCulture) ?? "provider-default");

app.Logger.LogInformation(
    "Provider max output tokens configuration. OpenAI={OpenAiTokens} Gemini={GeminiTokens} Anthropic={AnthropicTokens} TechnicalArchitect={TechnicalArchitectTokens} Synthesis={SynthesisTokens}",
    openAiMaxOutputTokens,
    geminiMaxOutputTokens,
    anthropicMaxOutputTokens,
    technicalArchitectMaxOutputTokens,
    synthesisMaxOutputTokens);

if (openAiTemperatureSetting.IsInvalid)
{
    app.Logger.LogWarning("OPENAI_TEMPERATURE is invalid. Expected decimal between 0 and 2. Falling back to provider default.");
}

if (geminiTemperatureSetting.IsInvalid)
{
    app.Logger.LogWarning("GEMINI_TEMPERATURE is invalid. Expected decimal between 0 and 2. Falling back to provider default.");
}

if (anthropicTemperatureSetting.IsInvalid)
{
    app.Logger.LogWarning("ANTHROPIC_TEMPERATURE is invalid. Expected decimal between 0 and 2. Falling back to provider default.");
}

if (technicalArchitectTemperatureSetting.IsInvalid)
{
    app.Logger.LogWarning("TECHNICAL_ARCHITECT_TEMPERATURE is invalid. Expected decimal between 0 and 2. Falling back to OpenAI temperature or provider default.");
}

if (openAiMaxOutputTokensSetting.IsInvalid)
{
    app.Logger.LogWarning("OPENAI_MAX_OUTPUT_TOKENS is invalid. Expected integer between 128 and 16384.");
}

if (geminiMaxOutputTokensSetting.IsInvalid)
{
    app.Logger.LogWarning("GEMINI_MAX_OUTPUT_TOKENS is invalid. Expected integer between 128 and 16384.");
}

if (anthropicMaxOutputTokensSetting.IsInvalid)
{
    app.Logger.LogWarning("ANTHROPIC_MAX_OUTPUT_TOKENS is invalid. Expected integer between 128 and 16384.");
}

if (technicalArchitectMaxOutputTokensSetting.IsInvalid)
{
    app.Logger.LogWarning("TECHNICAL_ARCHITECT_MAX_OUTPUT_TOKENS is invalid. Expected integer between 128 and 16384.");
}

if (synthesisMaxOutputTokensSetting.IsInvalid)
{
    app.Logger.LogWarning("SYNTHESIS_MAX_OUTPUT_TOKENS is invalid. Expected integer between 128 and 16384.");
}

if (string.IsNullOrWhiteSpace(openAiApiKey))
{
    app.Logger.LogWarning("OPENAI_KEY is not configured. openai-backed agents will emit system error messages.");
}

if (string.IsNullOrWhiteSpace(geminiApiKey))
{
    app.Logger.LogWarning("GEMINI_KEY is not configured. gemini-backed agents will emit system error messages.");
}

if (string.IsNullOrWhiteSpace(anthropicApiKey))
{
    app.Logger.LogWarning("ANTHROPIC_KEY/CLAUDE_KEY is not configured. anthropic-backed agents will emit system error messages.");
}

app.Logger.LogWarning(
    "Temporary provider override active: technical_architect is mapped to OpenAI model {Model} until DeepSeek billing is restored.",
    technicalArchitectModel);

app.MapPost("/sessions", async (
    CreateSessionRequest request,
    SessionService sessionService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("SessionsApi");
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

        return Results.Created($"/sessions/{session.SessionId}", new SessionResponse(
            session.SessionId,
            session.Status.ToString(),
            session.Phase.ToString(),
            session.Mode.ToString(),
            session.FrictionLevel,
            session.RoundNumber,
            session.TargetedLoopCount));
    }
    catch (ArgumentException exception)
    {
        logger.LogWarning(
            exception,
            "POST /sessions validation failed.");
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "POST /sessions failed unexpectedly.");
        throw;
    }
});

app.MapGet("/sessions/{sessionId:guid}", async (
    Guid sessionId,
    SessionService sessionService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("SessionsApi");
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
    return Results.Ok(new SessionResponse(
        session.SessionId,
        session.Status.ToString(),
        session.Phase.ToString(),
        session.Mode.ToString(),
        session.FrictionLevel,
        session.RoundNumber,
        session.TargetedLoopCount));
});

app.MapPost("/sessions/{sessionId:guid}/start", async (
    Guid sessionId,
    SessionService sessionService,
    HttpContext httpContext,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var correlationId = ResolveCorrelationId(httpContext);
    httpContext.Response.Headers[CorrelationHeaderName] = correlationId;

    var logger = loggerFactory.CreateLogger("SessionsApi");
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
        return Results.Ok(new SessionResponse(
            session.SessionId,
            session.Status.ToString(),
            session.Phase.ToString(),
            session.Mode.ToString(),
            session.FrictionLevel,
            session.RoundNumber,
            session.TargetedLoopCount));
    }
    catch (KeyNotFoundException)
    {
        logger.LogWarning(
            "POST /sessions/{SessionId}/start returned not found. CorrelationId={CorrelationId}",
            sessionId,
            correlationId);
        return Results.NotFound();
    }
    catch (Exception exception)
    {
        logger.LogError(
            exception,
            "POST /sessions/{SessionId}/start failed unexpectedly. CorrelationId={CorrelationId}",
            sessionId,
            correlationId);
        throw;
    }
});

app.MapPost("/sessions/{sessionId:guid}/messages", async (
    Guid sessionId,
    PostSessionMessageRequest request,
    SessionService sessionService,
    HttpContext httpContext,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var correlationId = ResolveCorrelationId(httpContext);
    httpContext.Response.Headers[CorrelationHeaderName] = correlationId;
    var logger = loggerFactory.CreateLogger("SessionsApi");

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
    catch (KeyNotFoundException)
    {
        logger.LogWarning(
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
    catch (Exception exception)
    {
        logger.LogError(
            exception,
            "POST /sessions/{SessionId}/messages failed unexpectedly. CorrelationId={CorrelationId}",
            sessionId,
            correlationId);
        throw;
    }
});

app.MapGet("/sessions/{sessionId:guid}/truthmap", async (
    Guid sessionId,
    SessionService sessionService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("SessionsApi");
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
});

app.MapGet("/sessions/{sessionId:guid}/transcript", async (
    Guid sessionId,
    SessionService sessionService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("SessionsApi");
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
});

app.MapHub<DebateHub>("/hubs/debate");

app.Run();

static string ResolveCorrelationId(HttpContext context)
{
    var fromHeader = context.Request.Headers[CorrelationHeaderName].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(fromHeader))
    {
        context.TraceIdentifier = fromHeader;
        return fromHeader;
    }

    return context.TraceIdentifier;
}

public record CreateSessionRequest(string Idea, string Mode, int FrictionLevel);

public record PostSessionMessageRequest(string? Message);

public record SessionResponse(
    Guid SessionId,
    string Status,
    string Phase,
    string Mode,
    int FrictionLevel,
    int RoundNumber,
    int TargetedLoopCount);

public record TruthMapResponse(
    Guid SessionId,
    int Version,
    int Round,
    string CoreIdea);

public record TranscriptMessageResponse(
    Guid Id,
    string Type,
    string? AgentId,
    string Content,
    int Round,
    bool IsStreaming,
    DateTimeOffset CreatedAtUtc);

public record SessionMessageResponse(
    Guid SessionId,
    string Phase,
    string RoutedAgentId,
    string Reply,
    bool PatchApplied);

public partial class Program;
