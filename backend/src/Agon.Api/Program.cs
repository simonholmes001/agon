using Agon.Api.Configuration;
using Agon.Api.Endpoints;
using Agon.Api.Middleware;
using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Services;
using Agon.Infrastructure.Persistence.InMemory;
using Agon.Infrastructure.SignalR;

var builder = WebApplication.CreateBuilder(args);
var isTestingEnvironment =
    builder.Environment.IsEnvironment("Test")
    || builder.Environment.IsEnvironment("Testing");

const string FrontendCorsPolicy = "FrontendCorsPolicy";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:3000", "https://localhost:3000"];
var dotEnvLoadResult = isTestingEnvironment
    ? new DotEnvLoadResult(null, 0, 0)
    : DotEnvLoader.Load(builder.Environment.ContentRootPath);

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

var providerConfig = ProviderConfiguration.Load(builder.Configuration);
builder.Services.AddCouncilAgents(providerConfig);

var app = builder.Build();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors(FrontendCorsPolicy);

LogStartupInformation(app, dotEnvLoadResult, providerConfig);

app.MapSessionEndpoints();
app.MapHub<DebateHub>("/hubs/debate");

await app.RunAsync();

static void LogStartupInformation(
    WebApplication app,
    DotEnvLoadResult dotEnvLoadResult,
    ProviderConfiguration providerConfig)
{
    if (dotEnvLoadResult.FilePath is not null)
    {
        app.Logger.LogInformation(
            ".env configuration loaded. Path={Path} LoadedKeys={LoadedKeys} SkippedExistingKeys={SkippedExistingKeys}",
            dotEnvLoadResult.FilePath,
            dotEnvLoadResult.LoadedCount,
            dotEnvLoadResult.SkippedExistingCount);
    }

    providerConfig.LogConfiguration(app.Logger);
    providerConfig.LogValidationWarnings(app.Logger);
    providerConfig.LogMissingApiKeyWarnings(app.Logger);
}

/// <summary>
/// Partial class declaration for test factory access.
/// </summary>
public sealed partial class Program
{
    private Program() { }
}
