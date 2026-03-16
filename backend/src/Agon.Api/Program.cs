using Agon.Api.Configuration;
using Agon.Api.Middleware;
using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Services;
using Agon.Domain.Agents;
using Agon.Domain.Engines;
using Agon.Domain.Sessions;
using Agon.Infrastructure.Agents;
using Agon.Infrastructure.Persistence.PostgreSQL;
using Agon.Infrastructure.Persistence.AzureBlob;
using Agon.Infrastructure.Persistence.Redis;
using Agon.Infrastructure.Persistence.Repositories;
using Agon.Infrastructure.SignalR;
using Anthropic;
using Google.GenAI;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using Mscc.GenerativeAI.Microsoft;
using OpenAI;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Load Environment Variables from .env File ───────────────────────────
// This allows us to read OPENAI_KEY, CLAUDE_KEY, etc. from the .env file in the root directory
// ContentRootPath is: /Users/.../Agon/backend/src/Agon.Api
// Need to go up: Agon.Api -> src -> backend -> Agon (root)
var contentRoot = builder.Environment.ContentRootPath;
var projectRoot = Directory.GetParent(contentRoot)?.Parent?.Parent?.FullName ?? "";
var envFilePath = Path.Combine(projectRoot, ".env");

Console.WriteLine($"[Startup] Looking for .env file at: {envFilePath}");
Console.WriteLine($"[Startup] .env file exists: {File.Exists(envFilePath)}");

if (File.Exists(envFilePath))
{
    var loadedKeys = new List<string>();
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
        
        var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            var key = parts[0];
            var value = parts[1].Trim('"'); // Remove surrounding quotes
            Environment.SetEnvironmentVariable(key, value);
            loadedKeys.Add(key);
        }
    }
    Console.WriteLine($"[Startup] Loaded {loadedKeys.Count} keys from .env: {string.Join(", ", loadedKeys)}");
}
else
{
    Console.WriteLine($"[Startup] WARNING: .env file not found at {envFilePath}");
}

// ── Configuration ───────────────────────────────────────────────────────
var llmConfig = builder.Configuration.GetSection(LlmConfiguration.SectionName).Get<LlmConfiguration>() ?? new();
var agonConfig = builder.Configuration.GetSection(AgonConfiguration.SectionName).Get<AgonConfiguration>() ?? new();
var authEnabled = builder.Configuration.GetValue<bool>("Authentication:Enabled");

// Replace ${ENV_VAR} placeholders with actual environment variables
llmConfig.OpenAI.ApiKey = ReplaceEnvVars(llmConfig.OpenAI.ApiKey);
llmConfig.Anthropic.ApiKey = ReplaceEnvVars(llmConfig.Anthropic.ApiKey);
llmConfig.Google.ApiKey = ReplaceEnvVars(llmConfig.Google.ApiKey);
llmConfig.DeepSeek.ApiKey = ReplaceEnvVars(llmConfig.DeepSeek.ApiKey);

Console.WriteLine($"[Startup] OpenAI API key configured: {!string.IsNullOrEmpty(llmConfig.OpenAI.ApiKey)}");
Console.WriteLine($"[Startup] Anthropic API key configured: {!string.IsNullOrEmpty(llmConfig.Anthropic.ApiKey)}");
Console.WriteLine($"[Startup] Google API key configured: {!string.IsNullOrEmpty(llmConfig.Google.ApiKey)}");
Console.WriteLine($"[Startup] DeepSeek API key configured: {!string.IsNullOrEmpty(llmConfig.DeepSeek.ApiKey)}");

builder.Services.AddSingleton(llmConfig);
builder.Services.AddSingleton(agonConfig);

if (authEnabled)
{
    var tenantId = builder.Configuration["Authentication:AzureAd:TenantId"] ?? string.Empty;
    var authority = builder.Configuration["Authentication:AzureAd:Authority"] ?? string.Empty;
    var audience = builder.Configuration["Authentication:AzureAd:Audience"] ?? string.Empty;
    var clientId = builder.Configuration["Authentication:AzureAd:ClientId"] ?? string.Empty;

    if (string.IsNullOrWhiteSpace(authority) && !string.IsNullOrWhiteSpace(tenantId))
    {
        authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
    }

    if (string.IsNullOrWhiteSpace(audience))
    {
        audience = clientId;
    }

    if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
    {
        throw new InvalidOperationException(
            "Authentication is enabled but Azure AD JWT settings are incomplete. Configure Authentication:AzureAd:Authority and Authentication:AzureAd:Audience (or ClientId).");
    }

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidAudiences = new[] { audience, clientId }.Where(value => !string.IsNullOrWhiteSpace(value))
            };
        });

    builder.Services.AddAuthorization();
}

// ── Controllers and OpenAPI ─────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Use camelCase for JSON property names (JavaScript convention)
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddOpenApi();

// ── SignalR for Real-Time Events ────────────────────────────────────────
builder.Services.AddSignalR();

// ── Database: PostgreSQL ────────────────────────────────────────────────
var postgresConnectionString = builder.Configuration.GetConnectionString("PostgreSQL");
if (!string.IsNullOrEmpty(postgresConnectionString))
{
    builder.Services.AddDbContext<AgonDbContext>(options =>
        options.UseNpgsql(postgresConnectionString));
    
    // ── Infrastructure: Repositories ────────────────────────────────────────
    builder.Services.AddScoped<ISessionRepository, SessionRepository>();
    builder.Services.AddScoped<ITruthMapRepository, TruthMapRepository>();
    builder.Services.AddScoped<IAgentMessageRepository, AgentMessageRepository>();
    builder.Services.AddScoped<IAttachmentRepository, AttachmentRepository>();
}

// ── Blob Storage: Session Attachments ───────────────────────────────────
var blobConnectionString = builder.Configuration.GetConnectionString("BlobStorage");
var attachmentContainerName = builder.Configuration["Storage:AttachmentContainer"] ?? "session-attachments";

if (!string.IsNullOrWhiteSpace(blobConnectionString))
{
    builder.Services.AddSingleton<IAttachmentStorageService>(_ =>
        new AzureBlobAttachmentStorageService(blobConnectionString, attachmentContainerName));
}

// ── Database: Redis ─────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        ConnectionMultiplexer.Connect(redisConnectionString));
    builder.Services.AddScoped<IDatabase>(sp =>
        sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
    
    builder.Services.AddScoped<ISnapshotStore, RedisSnapshotStore>();
}

// ── Infrastructure: SignalR Event Broadcasting ──────────────────────────
builder.Services.AddScoped<IEventBroadcaster, SignalREventBroadcaster>();

// ── Application Layer Services ──────────────────────────────────────────
// SessionService registered via factory so Lazy<IOrchestrator> is resolved from DI
// when available (non-Testing env), and null otherwise (Testing env).
builder.Services.AddScoped<ISessionService>(sp => new SessionService(
    sp.GetRequiredService<ISessionRepository>(),
    sp.GetRequiredService<ITruthMapRepository>(),
    sp.GetRequiredService<ISnapshotStore>(),
    sp.GetService<IAttachmentRepository>(),
    sp.GetService<IEventBroadcaster>(),
    sp.GetService<Lazy<IOrchestrator>>()));
builder.Services.AddScoped<ConversationHistoryService>();

// ── Domain: RoundPolicy (Session Configuration) ─────────────────────────
// Create RoundPolicy from configuration (immutable, so singleton is fine)
builder.Services.AddSingleton(sp =>
{
    var agonConfig = sp.GetRequiredService<AgonConfiguration>();
    return new RoundPolicy
    {
        MaxClarificationRounds = agonConfig.MaxClarificationRounds,
        MaxDebateRounds = agonConfig.MaxDebateRounds,
        MaxTargetedLoops = agonConfig.MaxTargetedLoops,
        MaxSessionBudgetTokens = agonConfig.SessionBudgetTokens,
        ConvergenceThresholdStandard = (float)agonConfig.ConvergenceThreshold,
        ConvergenceThresholdHighFriction = 0.85f, // Hard-coded high friction threshold
        HighFrictionCutoff = agonConfig.HighFrictionThreshold,
        ConfidenceDecay = new ConfidenceDecayConfig(), // Use defaults
        AgentTimeoutSeconds = 90
    };
});

// Only register Orchestrator and AgentRunner if dependencies are available
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddScoped<IAgentRunner, AgentRunner>();
    builder.Services.AddScoped<IOrchestrator, Orchestrator>();
    
    // Register Lazy<IOrchestrator> to break circular dependency with SessionService
    builder.Services.AddScoped<Lazy<IOrchestrator>>(sp => 
        new Lazy<IOrchestrator>(() => sp.GetRequiredService<IOrchestrator>()));
}

// ── Infrastructure: Council Agents (MAF with Multiple Providers) ────────
if (!builder.Environment.IsEnvironment("Testing"))
{
    // Register AgentResponseParser adapter (shared by all agents)
    builder.Services.AddScoped<IAgentResponseParser, AgentResponseParserAdapter>();

    // ── Register Council Agents with Provider-Specific Native MAF AIAgents ──
    
    // Moderator: OpenAI GPT (via ChatCompletion API)
    builder.Services.AddScoped<ICouncilAgent>(sp =>
    {
        var config = sp.GetRequiredService<LlmConfiguration>();
        var parser = sp.GetRequiredService<IAgentResponseParser>();
        
        IChatClient chatClient = new OpenAIClient(config.OpenAI.ApiKey)
            .GetChatClient(config.OpenAI.Model)
            .AsIChatClient();
        
        var aiAgent = chatClient.AsAIAgent(
                instructions: AgentSystemPrompts.Moderator,
                name: AgentId.Moderator);
        
        return new MafCouncilAgent(
            agentId: AgentId.Moderator,
            modelProvider: $"OpenAI {config.OpenAI.Model}",
            underlyingAgent: aiAgent,
            parser: parser);
    });

    // GPT Agent: OpenAI GPT (via ChatCompletion API)
    builder.Services.AddScoped<ICouncilAgent>(sp =>
    {
        var config = sp.GetRequiredService<LlmConfiguration>();
        var parser = sp.GetRequiredService<IAgentResponseParser>();
        
        IChatClient chatClient = new OpenAIClient(config.OpenAI.ApiKey)
            .GetChatClient(config.OpenAI.Model)
            .AsIChatClient();
        
        var aiAgent = chatClient.AsAIAgent(
                instructions: AgentSystemPrompts.GptAgent,
                name: AgentId.GptAgent);
        
        return new MafCouncilAgent(
            agentId: AgentId.GptAgent,
            modelProvider: $"OpenAI {config.OpenAI.Model}",
            underlyingAgent: aiAgent,
            parser: parser);
    });

    // Gemini Agent: Google Gemini (via Google.GenAI + Mscc.GenerativeAI.Microsoft)
    builder.Services.AddScoped<ICouncilAgent>(sp =>
    {
        var config = sp.GetRequiredService<LlmConfiguration>();
        var parser = sp.GetRequiredService<IAgentResponseParser>();
        var geminiClient = new Client(vertexAI: false, apiKey: config.Google.ApiKey);
        var chatClient = geminiClient.AsIChatClient(config.Google.Model);
        var aiAgent = chatClient.AsAIAgent(
            instructions: AgentSystemPrompts.GeminiAgent,
            name: AgentId.GeminiAgent);
        
        return new MafCouncilAgent(
            agentId: AgentId.GeminiAgent,
            modelProvider: $"Google {config.Google.Model}",
            underlyingAgent: aiAgent,
            parser: parser);
    });

    // Claude Agent: Anthropic Claude (via Anthropic SDK)
    builder.Services.AddScoped<ICouncilAgent>(sp =>
    {
        var config = sp.GetRequiredService<LlmConfiguration>();
        var parser = sp.GetRequiredService<IAgentResponseParser>();
        // Anthropic: .AsIChatClient(model) then .AsAIAgent()
        var aiAgent = new AnthropicClient() { ApiKey = config.Anthropic.ApiKey }
            .AsIChatClient(config.Anthropic.Model)
            .AsAIAgent(
                instructions: AgentSystemPrompts.ClaudeAgent,
                name: AgentId.ClaudeAgent);
        
        return new MafCouncilAgent(
            agentId: AgentId.ClaudeAgent,
            modelProvider: $"Anthropic {config.Anthropic.Model}",
            underlyingAgent: aiAgent,
            parser: parser);
    });

    // Synthesizer: OpenAI GPT (via ChatCompletion API)
    builder.Services.AddScoped<ICouncilAgent>(sp =>
    {
        var config = sp.GetRequiredService<LlmConfiguration>();
        var parser = sp.GetRequiredService<IAgentResponseParser>();
        
        IChatClient chatClient = new OpenAIClient(config.OpenAI.ApiKey)
            .GetChatClient(config.OpenAI.Model)
            .AsIChatClient();
        
        var aiAgent = chatClient.AsAIAgent(
                instructions: AgentSystemPrompts.Synthesizer,
                name: AgentId.Synthesizer);
        
        return new MafCouncilAgent(
            agentId: AgentId.Synthesizer,
            modelProvider: $"OpenAI {config.OpenAI.Model}",
            underlyingAgent: aiAgent,
            parser: parser);
    });

    // Post-delivery assistant: single-agent ChatGPT-style follow-up using OpenAI GPT
    builder.Services.AddScoped<ICouncilAgent>(sp =>
    {
        var config = sp.GetRequiredService<LlmConfiguration>();
        var parser = sp.GetRequiredService<IAgentResponseParser>();

        IChatClient chatClient = new OpenAIClient(config.OpenAI.ApiKey)
            .GetChatClient(config.OpenAI.Model)
            .AsIChatClient();

        var aiAgent = chatClient.AsAIAgent(
                instructions: AgentSystemPrompts.PostDeliveryAssistant,
                name: AgentId.PostDeliveryAssistant);

        return new MafCouncilAgent(
            agentId: AgentId.PostDeliveryAssistant,
            modelProvider: $"OpenAI {config.OpenAI.Model}",
            underlyingAgent: aiAgent,
            parser: parser);
    });

    // Register the collection of council agents
    builder.Services.AddScoped<IReadOnlyList<ICouncilAgent>>(sp =>
        sp.GetServices<ICouncilAgent>().ToList());
}
else
{
    // In test environment, provide empty list (tests mock ISessionService)
    builder.Services.AddScoped<IReadOnlyList<ICouncilAgent>>(sp => new List<ICouncilAgent>());
}

var app = builder.Build();

// ── HTTP Request Pipeline ───────────────────────────────────────────────

// Global exception handling (must be first in pipeline)
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<MinimumCliVersionMiddleware>();

// Apply EF Core migrations at startup so fresh environments have required schema.
using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Startup");

    var dbContext = scope.ServiceProvider.GetService<AgonDbContext>();
    if (dbContext is not null && dbContext.Database.IsRelational())
    {
        startupLogger.LogInformation("Applying PostgreSQL migrations...");
        dbContext.Database.Migrate();
        startupLogger.LogInformation("PostgreSQL migrations applied.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// ── Map Endpoints ───────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapControllers();
app.MapHub<DebateHub>("/hubs/debate");

app.Run();

// ── Helper Methods ──────────────────────────────────────────────────────

static string ReplaceEnvVars(string value)
{
    if (string.IsNullOrWhiteSpace(value)) return value;
    
    // Replace ${VAR_NAME} with environment variable value
    var pattern = @"\$\{([^}]+)\}";
    return System.Text.RegularExpressions.Regex.Replace(value, pattern, match =>
    {
        var envVarName = match.Groups[1].Value;
        return Environment.GetEnvironmentVariable(envVarName) ?? match.Value;
    });
}

// Make Program class accessible to integration tests
public partial class Program { }
