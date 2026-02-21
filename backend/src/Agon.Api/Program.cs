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

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "FrontendCorsPolicy";
const string OpenAiDefaultModel = "gpt-4o-mini";
const string GeminiDefaultModel = "gemini-2.0-flash";
const string AnthropicDefaultModel = "claude-3-5-sonnet-latest";
const string DeepSeekDefaultModel = "deepseek-chat";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:3000", "https://localhost:3000"];
var openAiApiKey = builder.Configuration["OPENAI_KEY"] ?? builder.Configuration["OpenAI:ApiKey"];
var openAiModel = builder.Configuration["OpenAI:Model"] ?? OpenAiDefaultModel;
var geminiApiKey = builder.Configuration["GEMINI_KEY"] ?? builder.Configuration["Gemini:ApiKey"];
var geminiModel = builder.Configuration["Gemini:Model"] ?? GeminiDefaultModel;
var anthropicApiKey = builder.Configuration["ANTHROPIC_KEY"]
    ?? builder.Configuration["CLAUDE_KEY"]
    ?? builder.Configuration["Anthropic:ApiKey"];
var anthropicModel = builder.Configuration["Anthropic:Model"] ?? AnthropicDefaultModel;
var deepSeekApiKey = builder.Configuration["DEEPSEEK_KEY"] ?? builder.Configuration["DeepSeek:ApiKey"];
var deepSeekModel = builder.Configuration["DeepSeek:Model"] ?? DeepSeekDefaultModel;

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
        MaxOutputTokens: 600);

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
            MaxOutputTokens: 600),
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
            MaxOutputTokens: 600),
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
            MaxOutputTokens: 600),
        sp.GetRequiredService<ILogger<AnthropicCouncilAgent>>());
});
builder.Services.AddSingleton<ICouncilAgent>(sp =>
{
    if (string.IsNullOrWhiteSpace(deepSeekApiKey))
    {
        return new ConfigurationErrorCouncilAgent(
            AgentId.TechnicalArchitect,
            modelProvider: "deepseek",
            errorMessage: "Missing DEEPSEEK_KEY for agent 'technical_architect'.",
            logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
    }

    return new DeepSeekCouncilAgent(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        new DeepSeekCouncilAgentOptions(
            AgentId.TechnicalArchitect,
            deepSeekApiKey,
            deepSeekModel,
            MaxOutputTokens: 600),
        sp.GetRequiredService<ILogger<DeepSeekCouncilAgent>>());
});

var app = builder.Build();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors(FrontendCorsPolicy);

app.Logger.LogInformation(
    "Council provider configuration. OpenAI={OpenAiConfigured} Gemini={GeminiConfigured} Anthropic={AnthropicConfigured} DeepSeek={DeepSeekConfigured}",
    !string.IsNullOrWhiteSpace(openAiApiKey),
    !string.IsNullOrWhiteSpace(geminiApiKey),
    !string.IsNullOrWhiteSpace(anthropicApiKey),
    !string.IsNullOrWhiteSpace(deepSeekApiKey));

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

if (string.IsNullOrWhiteSpace(deepSeekApiKey))
{
    app.Logger.LogWarning("DEEPSEEK_KEY is not configured. deepseek-backed agents will emit system error messages.");
}

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
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("SessionsApi");
    logger.LogInformation("POST /sessions/{SessionId}/start received.", sessionId);

    try
    {
        var session = await sessionService.StartSessionAsync(sessionId, cancellationToken);
        logger.LogInformation(
            "POST /sessions/{SessionId}/start transitioned to phase {Phase}.",
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
    }
    catch (KeyNotFoundException)
    {
        logger.LogWarning("POST /sessions/{SessionId}/start returned not found.", sessionId);
        return Results.NotFound();
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "POST /sessions/{SessionId}/start failed unexpectedly.", sessionId);
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

public record CreateSessionRequest(string Idea, string Mode, int FrictionLevel);

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

public partial class Program;
