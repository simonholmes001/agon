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
using Agon.Infrastructure.Attachments;
using Anthropic;
using Azure.Core;
using Azure.Identity;
using Google.GenAI;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using Mscc.GenerativeAI.Microsoft;
using Npgsql;
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
var envFileExists = File.Exists(envFilePath);
var envKeysLoaded = 0;

if (File.Exists(envFilePath))
{
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
        
        var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            var key = parts[0];
            var value = parts[1].Trim('"'); // Remove surrounding quotes
            Environment.SetEnvironmentVariable(key, value);
            envKeysLoaded++;
        }
    }
}

// ── Configuration ───────────────────────────────────────────────────────
var llmConfig = builder.Configuration.GetSection(LlmConfiguration.SectionName).Get<LlmConfiguration>() ?? new();
var agonConfig = builder.Configuration.GetSection(AgonConfiguration.SectionName).Get<AgonConfiguration>() ?? new();
var attachmentProcessingConfig = builder.Configuration
    .GetSection(AttachmentProcessingConfiguration.SectionName)
    .Get<AttachmentProcessingConfiguration>() ?? new();
var authEnabled = builder.Configuration.GetValue<bool>("Authentication:Enabled");
var allowedCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

// Replace ${ENV_VAR} placeholders with actual environment variables
llmConfig.OpenAI.ApiKey = ReplaceEnvVars(llmConfig.OpenAI.ApiKey);
llmConfig.Anthropic.ApiKey = ReplaceEnvVars(llmConfig.Anthropic.ApiKey);
llmConfig.Google.ApiKey = ReplaceEnvVars(llmConfig.Google.ApiKey);
llmConfig.DeepSeek.ApiKey = ReplaceEnvVars(llmConfig.DeepSeek.ApiKey);
attachmentProcessingConfig.DocumentIntelligence.Endpoint = ReplaceEnvVars(attachmentProcessingConfig.DocumentIntelligence.Endpoint);
attachmentProcessingConfig.DocumentIntelligence.ApiKey = ReplaceEnvVars(attachmentProcessingConfig.DocumentIntelligence.ApiKey);

builder.Services.AddSingleton(llmConfig);
builder.Services.AddSingleton(agonConfig);
builder.Services.AddSingleton(new AttachmentExtractionOptions
{
    MaxExtractedTextChars = attachmentProcessingConfig.MaxExtractedTextChars,
    DocumentIntelligence = new DocumentIntelligenceExtractionOptions
    {
        Enabled = attachmentProcessingConfig.DocumentIntelligence.Enabled,
        Endpoint = attachmentProcessingConfig.DocumentIntelligence.Endpoint,
        ModelId = attachmentProcessingConfig.DocumentIntelligence.ModelId,
        ApiVersion = attachmentProcessingConfig.DocumentIntelligence.ApiVersion,
        UseManagedIdentity = attachmentProcessingConfig.DocumentIntelligence.UseManagedIdentity,
        ApiKey = attachmentProcessingConfig.DocumentIntelligence.ApiKey,
        PollIntervalMs = attachmentProcessingConfig.DocumentIntelligence.PollIntervalMs,
        MaxPollAttempts = attachmentProcessingConfig.DocumentIntelligence.MaxPollAttempts
    },
    OpenAiVision = new OpenAiVisionExtractionOptions
    {
        Enabled = attachmentProcessingConfig.OpenAiVision.Enabled,
        ApiKey = llmConfig.OpenAI.ApiKey,
        Model = attachmentProcessingConfig.OpenAiVision.Model,
        MaxTokens = attachmentProcessingConfig.OpenAiVision.MaxTokens,
        Detail = attachmentProcessingConfig.OpenAiVision.Detail,
        MaxImageBytes = attachmentProcessingConfig.OpenAiVision.MaxImageBytes
    }
});
builder.Services.AddHttpClient("attachment-extraction", client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddScoped<IAttachmentTextExtractor, AttachmentTextExtractor>();

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
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidAudiences = new[] { audience, clientId }.Where(value => !string.IsNullOrWhiteSpace(value))
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });
}

// ── Controllers and OpenAPI ─────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Use camelCase for JSON property names (JavaScript convention)
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AgonCors", policy =>
    {
        if (allowedCorsOrigins.Length > 0)
        {
            policy.WithOrigins(allowedCorsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowAnyOrigin();
    });
});

// ── SignalR for Real-Time Events ────────────────────────────────────────
builder.Services.AddSignalR();

// ── Database: PostgreSQL ────────────────────────────────────────────────
var postgresConnectionString = builder.Configuration.GetConnectionString("PostgreSQL");
var usePostgresManagedIdentity = builder.Configuration.GetValue<bool>("Database:PostgreSql:UseManagedIdentity");
if (usePostgresManagedIdentity)
{
    var postgresManagedIdentityConnectionString = BuildPostgresManagedIdentityConnectionString(builder.Configuration);
    if (string.IsNullOrWhiteSpace(postgresManagedIdentityConnectionString))
    {
        throw new InvalidOperationException(
            "Database:PostgreSql:UseManagedIdentity is enabled but PostgreSQL host/database/username settings are incomplete.");
    }

    builder.Services.AddSingleton(_ => CreatePostgresManagedIdentityDataSource(postgresManagedIdentityConnectionString));
    builder.Services.AddDbContext<AgonDbContext>((sp, options) =>
        options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));

    RegisterPersistenceServices(builder.Services);
}
else if (!string.IsNullOrEmpty(postgresConnectionString))
{
    builder.Services.AddDbContext<AgonDbContext>(options =>
        options.UseNpgsql(postgresConnectionString));

    RegisterPersistenceServices(builder.Services);
}

// ── Blob Storage: Session Attachments ───────────────────────────────────
var blobConnectionString = ReplaceEnvVars(builder.Configuration.GetConnectionString("BlobStorage") ?? string.Empty);
var attachmentContainerName = builder.Configuration["Storage:AttachmentContainer"] ?? "session-attachments";

if (IsConfiguredValue(blobConnectionString))
{
    builder.Services.AddSingleton<IAttachmentStorageService>(_ =>
        new AzureBlobAttachmentStorageService(blobConnectionString, attachmentContainerName));
}

// ── Database: Redis ─────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
var useRedisManagedIdentity = builder.Configuration.GetValue<bool>("Redis:UseManagedIdentity");
if (!builder.Environment.IsEnvironment("Testing"))
{
    if (useRedisManagedIdentity)
    {
        var redisHost = builder.Configuration["Redis:Host"];
        var redisPort = builder.Configuration.GetValue<int?>("Redis:Port") ?? 6380;

        if (string.IsNullOrWhiteSpace(redisHost))
        {
            throw new InvalidOperationException(
                "Redis:UseManagedIdentity is enabled but Redis:Host is missing.");
        }

        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            CreateRedisManagedIdentityConnection(redisHost, redisPort));
    }
    else
    {
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnectionString));
    }

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

app.Logger.LogInformation(
    "Startup summary: EnvFileExists={EnvFileExists}, EnvKeysLoaded={EnvKeysLoaded}, AuthEnabled={AuthEnabled}, CorsOriginCount={CorsOriginCount}, OpenAIConfigured={OpenAIConfigured}, AnthropicConfigured={AnthropicConfigured}, GoogleConfigured={GoogleConfigured}, DeepSeekConfigured={DeepSeekConfigured}, DocumentIntelligenceEndpointConfigured={DocumentIntelligenceEndpointConfigured}",
    envFileExists,
    envKeysLoaded,
    authEnabled,
    allowedCorsOrigins.Length,
    !string.IsNullOrEmpty(llmConfig.OpenAI.ApiKey),
    !string.IsNullOrEmpty(llmConfig.Anthropic.ApiKey),
    !string.IsNullOrEmpty(llmConfig.Google.ApiKey),
    !string.IsNullOrEmpty(llmConfig.DeepSeek.ApiKey),
    !string.IsNullOrWhiteSpace(attachmentProcessingConfig.DocumentIntelligence.Endpoint));

if (allowedCorsOrigins.Length == 0)
{
    app.Logger.LogWarning("No CORS origins configured. AllowAnyOrigin policy is active.");
}

app.UseHttpsRedirection();
app.UseCors("AgonCors");
if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// ── Map Endpoints ───────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();
var controllers = app.MapControllers();
if (authEnabled)
{
    controllers.RequireAuthorization();
}
var debateHub = app.MapHub<DebateHub>("/hubs/debate");
if (authEnabled)
{
    debateHub.RequireAuthorization();
}

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

static bool IsConfiguredValue(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    var trimmed = value.Trim();
    return !(trimmed.StartsWith("${", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal));
}

static void RegisterPersistenceServices(IServiceCollection services)
{
    services.AddScoped<ISessionRepository, SessionRepository>();
    services.AddScoped<ITruthMapRepository, TruthMapRepository>();
    services.AddScoped<IAgentMessageRepository, AgentMessageRepository>();
    services.AddScoped<IAttachmentRepository, AttachmentRepository>();
}

static string BuildPostgresManagedIdentityConnectionString(IConfiguration configuration)
{
    var host = configuration["Database:PostgreSql:Host"];
    var database = configuration["Database:PostgreSql:Database"] ?? "agon";
    var username = configuration["Database:PostgreSql:Username"];
    var port = configuration.GetValue<int?>("Database:PostgreSql:Port") ?? 5432;

    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(username))
    {
        return string.Empty;
    }

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = host,
        Port = port,
        Database = database,
        Username = username,
        SslMode = SslMode.Require,
        IncludeErrorDetail = true
    };

    return builder.ConnectionString;
}

static NpgsqlDataSource CreatePostgresManagedIdentityDataSource(string connectionString)
{
    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeInteractiveBrowserCredential = true
    });
    var tokenRequestContext = new TokenRequestContext(new[]
    {
        "https://ossrdbms-aad.database.windows.net/.default"
    });

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.UsePeriodicPasswordProvider(
        async (_, cancellationToken) =>
        {
            var token = await credential.GetTokenAsync(tokenRequestContext, cancellationToken);
            return token.Token;
        },
        TimeSpan.FromMinutes(50),
        TimeSpan.FromSeconds(30));

    return dataSourceBuilder.Build();
}

static IConnectionMultiplexer CreateRedisManagedIdentityConnection(string host, int port)
{
    var options = new ConfigurationOptions
    {
        Ssl = true,
        AbortOnConnectFail = false
    };
    options.EndPoints.Add(host, port);

    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeInteractiveBrowserCredential = true
    });

    options.ConfigureForAzureWithTokenCredentialAsync(credential).GetAwaiter().GetResult();
    return ConnectionMultiplexer.Connect(options);
}

// Make Program class accessible to integration tests
public partial class Program { }
