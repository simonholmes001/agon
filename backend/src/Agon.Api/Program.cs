using Agon.Api.Configuration;
using Agon.Api.Endpoints;
using Agon.Api.Middleware;
using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Services;
using Agon.Infrastructure.Persistence.InMemory;
using Agon.Infrastructure.SignalR;
using Serilog;
using Serilog.Events;

// Bootstrap logger catches startup errors before the full Serilog pipeline is configured.
// Use CreateLogger (not CreateBootstrapLogger) so the static Log.Logger is not frozen when
// WebApplicationFactory creates a second host instance during integration tests.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

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

// Replace the default Microsoft logging with Serilog.
builder.Host.UseSerilog((ctx, services, config) => config
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .MinimumLevel.Debug()
    // Quiet down chatty framework namespaces
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Cors", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.StaticFiles", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
        restrictedToMinimumLevel: LogEventLevel.Debug));

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
builder.Services.AddSingleton<ArtifactService>();
builder.Services.AddHttpClient();

// Artifact generators
builder.Services.AddSingleton<IArtifactGenerator, CopilotInstructionGenerator>();
builder.Services.AddSingleton<IArtifactGenerator, ArchitectureInstructionGenerator>();
builder.Services.AddSingleton<IArtifactGenerator, PrdInstructionGenerator>();
builder.Services.AddSingleton<IArtifactGenerator, RiskRegistryGenerator>();
builder.Services.AddSingleton<IArtifactGenerator, AssumptionValidationGenerator>();

var providerConfig = ProviderConfiguration.Load(builder.Configuration);
builder.Services.AddCouncilAgents(providerConfig);

var app = builder.Build();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors(FrontendCorsPolicy);

// Serilog request logging — logs each HTTP request with method, path, status code and elapsed time.
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "unknown");
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        var correlationId = httpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? httpContext.TraceIdentifier;
        diagnosticContext.Set("CorrelationId", correlationId);
    };
});

LogStartupInformation(app, dotEnvLoadResult, providerConfig);

app.MapSessionEndpoints();
app.MapArtifactEndpoints();
app.MapHub<DebateHub>("/hubs/debate");

try
{
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException and not OperationCanceledException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

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
