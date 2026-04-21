using Agon.Api.Configuration;
using Agon.Api.Middleware;
using Agon.Api.Auth;
using Agon.Api.Observability;
using Agon.Api.Services;
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
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.Features;

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
var storageConfig = builder.Configuration
    .GetSection(StorageConfiguration.SectionName)
    .Get<StorageConfiguration>() ?? new();
var attachmentOperationsConfig = builder.Configuration
    .GetSection(AttachmentOperationsConfiguration.SectionName)
    .Get<AttachmentOperationsConfiguration>() ?? new();
var rateLimitingConfig = builder.Configuration
    .GetSection(ApiRateLimitingConfiguration.SectionName)
    .Get<ApiRateLimitingConfiguration>() ?? new();
var trialAccessConfig = builder.Configuration
    .GetSection(TrialAccessConfiguration.SectionName)
    .Get<TrialAccessConfiguration>() ?? new();
var trialAccessModeRaw = builder.Configuration["TrialAccess:AccessMode"];
var forceRateLimitingInTesting = builder.Configuration.GetValue<bool>("ApiRateLimiting:ForceEnableInTesting");
if (builder.Environment.IsEnvironment("Testing") && !forceRateLimitingInTesting)
{
    rateLimitingConfig.Enabled = false;
}
var authEnabled = builder.Configuration.GetValue<bool>("Authentication:Enabled");
var authTenantId = builder.Configuration["Authentication:AzureAd:TenantId"] ?? string.Empty;
var authAuthority = builder.Configuration["Authentication:AzureAd:Authority"] ?? string.Empty;
var authAudience = builder.Configuration["Authentication:AzureAd:Audience"] ?? string.Empty;
var authClientId = builder.Configuration["Authentication:AzureAd:ClientId"] ?? string.Empty;
var authInteractiveClientId = builder.Configuration["Authentication:AzureAd:InteractiveClientId"] ?? string.Empty;
var allowedCorsOrigins = ResolveAllowedCorsOrigins(builder.Configuration);
var normalizedAuthAudience = NormalizeAudience(authAudience);
var normalizedAuthClientId = NormalizeAudience(authClientId);
var derivedClientId = ResolveClientIdFromAudience(normalizedAuthAudience);
var effectiveTenantId = ResolveTenantId(authTenantId, authAuthority);

if (string.IsNullOrWhiteSpace(normalizedAuthClientId) && !string.IsNullOrWhiteSpace(derivedClientId))
{
    normalizedAuthClientId = derivedClientId;
}

if (string.IsNullOrWhiteSpace(authAuthority) && !string.IsNullOrWhiteSpace(authTenantId))
{
    authAuthority = $"https://login.microsoftonline.com/{authTenantId}/v2.0";
}

if (string.IsNullOrWhiteSpace(authAudience))
{
    authAudience = authClientId;
}

// ── Non-development startup fail-fast guards ─────────────────────────────
// These guards prevent accidental insecure deployments outside local dev/test.
//
// NOTE: The "Testing" environment is only treated as safe when
// Security:AllowInsecureTestingMode=true is explicitly set in configuration.
// This prevents a real deployment accidentally configured with
// ASPNETCORE_ENVIRONMENT=Testing from bypassing authentication and CORS guards.
var allowInsecureTestingMode = builder.Configuration.GetValue<bool>("Security:AllowInsecureTestingMode");
var isNonProductionSafeEnvironment = builder.Environment.IsDevelopment()
    || (builder.Environment.IsEnvironment("Testing") && allowInsecureTestingMode);

ValidateTrialAccessAccessMode(trialAccessModeRaw, isNonProductionSafeEnvironment);

if (!isNonProductionSafeEnvironment)
{
    if (!authEnabled)
    {
        throw new InvalidOperationException(
            "SECURITY: Authentication:Enabled must be true in non-development environments. " +
            "Set Authentication:Enabled=true and configure Authentication:AzureAd:Authority and Authentication:AzureAd:Audience.");
    }

    if (string.IsNullOrWhiteSpace(authAuthority) || string.IsNullOrWhiteSpace(authAudience))
    {
        throw new InvalidOperationException(
            "SECURITY: Authentication:AzureAd:Authority and Authentication:AzureAd:Audience (or ClientId) " +
            "must be configured in non-development environments.");
    }

    if (allowedCorsOrigins.Length == 0)
    {
        throw new InvalidOperationException(
            "SECURITY: Cors:AllowedOrigins must not be empty in non-development environments. " +
            "Configure at least one trusted origin to prevent cross-origin exposure.");
    }
}

// Replace ${ENV_VAR} placeholders with actual environment variables
llmConfig.OpenAI.ApiKey = ReplaceEnvVars(llmConfig.OpenAI.ApiKey);
llmConfig.Anthropic.ApiKey = ReplaceEnvVars(llmConfig.Anthropic.ApiKey);
llmConfig.Google.ApiKey = ReplaceEnvVars(llmConfig.Google.ApiKey);
llmConfig.DeepSeek.ApiKey = ReplaceEnvVars(llmConfig.DeepSeek.ApiKey);
attachmentProcessingConfig.DocumentIntelligence.Endpoint = ReplaceEnvVars(attachmentProcessingConfig.DocumentIntelligence.Endpoint);
attachmentProcessingConfig.DocumentIntelligence.ApiKey = ReplaceEnvVars(attachmentProcessingConfig.DocumentIntelligence.ApiKey);
attachmentProcessingConfig.OpenAiVision.FallbackModel = ReplaceEnvVars(attachmentProcessingConfig.OpenAiVision.FallbackModel);
storageConfig.AttachmentBlobServiceUri = ReplaceEnvVars(storageConfig.AttachmentBlobServiceUri);

llmConfig.OpenAI.ApiKey = NormalizeSecretValue(llmConfig.OpenAI.ApiKey);
llmConfig.Anthropic.ApiKey = NormalizeSecretValue(llmConfig.Anthropic.ApiKey);
llmConfig.Google.ApiKey = NormalizeSecretValue(llmConfig.Google.ApiKey);
llmConfig.DeepSeek.ApiKey = NormalizeSecretValue(llmConfig.DeepSeek.ApiKey);
attachmentProcessingConfig.DocumentIntelligence.ApiKey = NormalizeSecretValue(attachmentProcessingConfig.DocumentIntelligence.ApiKey);

builder.Services.AddSingleton(llmConfig);
builder.Services.AddSingleton(agonConfig);
builder.Services.AddSingleton(attachmentOperationsConfig);
builder.Services.AddSingleton(rateLimitingConfig);
builder.Services.AddSingleton(trialAccessConfig);
builder.Services.AddSingleton<ITokenUsageRepository, NoOpTokenUsageRepository>();
builder.Services.AddSingleton<TrialRequestRateLimiter>();
builder.Services.AddScoped<TrialAccessService>();
var maxUploadBytes = Math.Max(1, attachmentProcessingConfig.Validation.MaxUploadBytes);
builder.Services.AddSingleton(new AttachmentUploadValidationOptions
{
    RejectUnsupportedFormats = attachmentProcessingConfig.Validation.RejectUnsupportedFormats,
    MaxUploadBytes = maxUploadBytes,
    MaxTextUploadBytes = Math.Max(1, Math.Min(maxUploadBytes, attachmentProcessingConfig.Validation.MaxTextUploadBytes)),
    MaxDocumentUploadBytes = Math.Max(1, Math.Min(maxUploadBytes, attachmentProcessingConfig.Validation.MaxDocumentUploadBytes)),
    MaxImageUploadBytes = Math.Max(1, Math.Min(maxUploadBytes, attachmentProcessingConfig.Validation.MaxImageUploadBytes))
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes + (1 * 1024 * 1024);
});
var asyncBatchSize = attachmentProcessingConfig.AsyncExtraction.BatchSize > 0
    ? attachmentProcessingConfig.AsyncExtraction.BatchSize
    : Math.Max(1, attachmentProcessingConfig.AsyncExtraction.QueueCapacity);
builder.Services.AddSingleton(new AttachmentAsyncExtractionOptions
{
    Enabled = attachmentProcessingConfig.AsyncExtraction.Enabled,
    BatchSize = asyncBatchSize,
    PollIntervalMs = attachmentProcessingConfig.AsyncExtraction.PollIntervalMs,
    RequeueStaleExtractingEnabled = attachmentProcessingConfig.AsyncExtraction.RequeueStaleExtractingEnabled,
    StaleExtractingAfterMinutes = attachmentProcessingConfig.AsyncExtraction.StaleExtractingAfterMinutes,
    ReconcileIntervalMs = attachmentProcessingConfig.AsyncExtraction.ReconcileIntervalMs
});
builder.Services.AddSingleton(new AttachmentChunkLoopOptions
{
    Enabled = attachmentProcessingConfig.ChunkLoop.Enabled,
    ActivationThresholdChars = attachmentProcessingConfig.ChunkLoop.ActivationThresholdChars,
    ChunkSizeChars = attachmentProcessingConfig.ChunkLoop.ChunkSizeChars,
    ChunkOverlapChars = attachmentProcessingConfig.ChunkLoop.ChunkOverlapChars,
    UseTokenAwareSizing = attachmentProcessingConfig.ChunkLoop.UseTokenAwareSizing,
    TargetChunkTokens = attachmentProcessingConfig.ChunkLoop.TargetChunkTokens,
    EstimatedCharsPerToken = attachmentProcessingConfig.ChunkLoop.EstimatedCharsPerToken,
    EnableQueryFocusedSecondPass = attachmentProcessingConfig.ChunkLoop.EnableQueryFocusedSecondPass,
    MaxFocusedChunksPerAttachment = attachmentProcessingConfig.ChunkLoop.MaxFocusedChunksPerAttachment,
    MinQueryKeywordLength = attachmentProcessingConfig.ChunkLoop.MinQueryKeywordLength,
    MaxChunksPerAttachment = attachmentProcessingConfig.ChunkLoop.MaxChunksPerAttachment,
    MaxChunkNoteChars = attachmentProcessingConfig.ChunkLoop.MaxChunkNoteChars,
    MaxFinalNotesPerAgent = attachmentProcessingConfig.ChunkLoop.MaxFinalNotesPerAgent,
    MaxPreludePasses = attachmentProcessingConfig.ChunkLoop.MaxPreludePasses,
    MaxChunkBudgetChars = attachmentProcessingConfig.ChunkLoop.MaxChunkBudgetChars,
    EarlyExitMinNotesPerAgent = attachmentProcessingConfig.ChunkLoop.EarlyExitMinNotesPerAgent
});
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
        FallbackModel = string.IsNullOrWhiteSpace(attachmentProcessingConfig.OpenAiVision.FallbackModel)
            ? llmConfig.OpenAI.Model
            : attachmentProcessingConfig.OpenAiVision.FallbackModel,
        MaxTokens = attachmentProcessingConfig.OpenAiVision.MaxTokens,
        Detail = attachmentProcessingConfig.OpenAiVision.Detail,
        MaxImageBytes = attachmentProcessingConfig.OpenAiVision.MaxImageBytes
    },
    TransientRetry = new AttachmentTransientRetryOptions
    {
        MaxAttempts = attachmentProcessingConfig.TransientRetry.MaxAttempts,
        BaseDelayMs = attachmentProcessingConfig.TransientRetry.BaseDelayMs,
        MaxDelayMs = attachmentProcessingConfig.TransientRetry.MaxDelayMs
    }
});
builder.Services.AddHttpClient("attachment-extraction", client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddScoped<IAttachmentTextExtractor, AttachmentTextExtractor>();
builder.Services.AddScoped<IDocumentParser, DocumentParseService>();
// Canonical async extraction host. Legacy AttachmentExtractionWorker/queue types are intentionally not hosted.
builder.Services.AddHostedService<AttachmentExtractionWorkerService>();

if (authEnabled)
{
    if (string.IsNullOrWhiteSpace(authAuthority) || string.IsNullOrWhiteSpace(authAudience))
    {
        throw new InvalidOperationException(
            "Authentication is enabled but Azure AD JWT settings are incomplete. Configure Authentication:AzureAd:Authority and Authentication:AzureAd:Audience (or ClientId).");
    }

    var validAudiences = BuildValidAudiences(normalizedAuthAudience, normalizedAuthClientId);
    var validIssuers = BuildValidIssuers(authAuthority, effectiveTenantId);

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authAuthority;
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidAudiences = validAudiences,
                ValidIssuers = validIssuers
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

        // Only allow the permissive fallback in local development/testing.
        // Non-dev environments are already blocked at startup (fail-fast guard above).
        if (isNonProductionSafeEnvironment)
        {
            policy.AllowAnyHeader()
                .AllowAnyMethod()
                .AllowAnyOrigin();
        }
    });
});

if (rateLimitingConfig.Enabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        // Controllers use [EnableRateLimiting("<policy-name>")] attributes.
        // Register matching named policies to avoid runtime 409 errors when
        // endpoint metadata references a policy that does not exist.
        options.AddPolicy<string>("session-create", context =>
        {
            var partitionKey = $"session-create:{ResolveRateLimitPartitionKey(context)}";
            return BuildFixedWindowRateLimiter(partitionKey, rateLimitingConfig.SessionCreate);
        });
        options.AddPolicy<string>("session-message", context =>
        {
            var partitionKey = $"session-message:{ResolveRateLimitPartitionKey(context)}";
            return BuildFixedWindowRateLimiter(partitionKey, rateLimitingConfig.SessionMessage);
        });
        options.AddPolicy<string>("attachment-upload", context =>
        {
            var partitionKey = $"attachment-upload:{ResolveRateLimitPartitionKey(context)}";
            return BuildFixedWindowRateLimiter(partitionKey, rateLimitingConfig.AttachmentUpload);
        });

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            var endpoint = context.HttpContext.Request.Path.Value ?? "unknown";
            var method = context.HttpContext.Request.Method;
            var partitionKey = ResolveRateLimitPartitionKey(context.HttpContext);

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter) && retryAfter > TimeSpan.Zero)
            {
                context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                    .ToString(CultureInfo.InvariantCulture);
            }

            AttachmentMetrics.RateLimitRejected.Add(
                1,
                new KeyValuePair<string, object?>("endpoint", endpoint),
                new KeyValuePair<string, object?>("method", method));

            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("RateLimiting");
            logger.LogWarning(
                "Rate limit rejected request. Endpoint={Endpoint}, Method={Method}, Key={PartitionKey}",
                endpoint,
                method,
                partitionKey);

            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                errorCode = "RATE_LIMIT_EXCEEDED",
                error = "Too many requests. Please retry later.",
                endpoint
            }, cancellationToken);
        };
    });
}

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
var attachmentStorageMode = ConfigureAttachmentStorage(builder.Services, builder.Configuration, storageConfig);

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
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<AttachmentRetentionCleanupService>();
}

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
// Strip sensitive provider key headers early — before any logging, telemetry, or handlers.
app.UseMiddleware<SensitiveHeaderStrippingMiddleware>();
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
        EnsureSessionsCouncilRunColumns(dbContext, startupLogger);
        startupLogger.LogInformation("PostgreSQL migrations applied.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Logger.LogInformation(
    "Startup summary: EnvFileExists={EnvFileExists}, EnvKeysLoaded={EnvKeysLoaded}, AuthEnabled={AuthEnabled}, CorsOriginCount={CorsOriginCount}, OpenAIConfigured={OpenAIConfigured}, AnthropicConfigured={AnthropicConfigured}, GoogleConfigured={GoogleConfigured}, DeepSeekConfigured={DeepSeekConfigured}, DocumentIntelligenceEndpointConfigured={DocumentIntelligenceEndpointConfigured}, AttachmentStorageMode={AttachmentStorageMode}, ApiRateLimitingEnabled={ApiRateLimitingEnabled}, TrialAccessEnabled={TrialAccessEnabled}, AttachmentRetentionDays={AttachmentRetentionDays}, AttachmentCleanupEnabled={AttachmentCleanupEnabled}",
    envFileExists,
    envKeysLoaded,
    authEnabled,
    allowedCorsOrigins.Length,
    !string.IsNullOrEmpty(llmConfig.OpenAI.ApiKey),
    !string.IsNullOrEmpty(llmConfig.Anthropic.ApiKey),
    !string.IsNullOrEmpty(llmConfig.Google.ApiKey),
    !string.IsNullOrEmpty(llmConfig.DeepSeek.ApiKey),
    !string.IsNullOrWhiteSpace(attachmentProcessingConfig.DocumentIntelligence.Endpoint),
    attachmentStorageMode,
    rateLimitingConfig.Enabled,
    trialAccessConfig.Enabled,
    attachmentOperationsConfig.Retention.RetentionDays,
    attachmentOperationsConfig.Retention.CleanupEnabled);

if (allowedCorsOrigins.Length == 0)
{
    app.Logger.LogWarning(
        "No CORS origins configured. AllowAnyOrigin policy is active (development/testing only). " +
        "Set Cors:AllowedOrigins via array keys (Cors__AllowedOrigins__0) or comma-separated value (Cors__AllowedOrigins).");
}

if (attachmentStorageMode == "disabled")
{
    app.Logger.LogWarning(
        "Attachment storage is not configured. /attach uploads will return 503. Configure ConnectionStrings:BlobStorage (local) or Storage:UseManagedIdentity=true with Storage:AttachmentBlobServiceUri (Azure).");
}

app.UseHttpsRedirection();
app.UseCors("AgonCors");
if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
if (rateLimitingConfig.Enabled)
{
    app.UseRateLimiter();
}

// ── Map Endpoints ───────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();

// Always anonymous: clients use this to discover whether the backend requires a bearer token
// before making any authenticated API calls. Returns { required: bool, scheme: string }.
var authStatusPayload = AuthStatusResponseFactory.Create(
    authEnabled: authEnabled,
    authority: authAuthority,
    audience: authAudience,
    tenantIdHint: authTenantId,
    interactiveClientId: authInteractiveClientId);
app.MapGet("/auth/status", () => Results.Ok(authStatusPayload)).AllowAnonymous();
app.MapGet("/auth/verify", (ClaimsPrincipal user) =>
{
    var claimValue =
        user.FindFirstValue("oid")
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue("sub");

    return Results.Ok(new
    {
        authenticated = user.Identity?.IsAuthenticated ?? false,
        userId = claimValue
    });
});
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

static void EnsureSessionsCouncilRunColumns(AgonDbContext dbContext, ILogger startupLogger)
{
    // Defensive startup guard: schema drift has previously caused runtime 500s when
    // the code expects council_run_* columns but the sessions table is missing them.
    const string ensureColumnsSql = """
        ALTER TABLE IF EXISTS sessions
            ADD COLUMN IF NOT EXISTS council_run_phase text,
            ADD COLUMN IF NOT EXISTS council_run_started_at timestamp with time zone,
            ADD COLUMN IF NOT EXISTS council_run_first_progress_at timestamp with time zone,
            ADD COLUMN IF NOT EXISTS council_run_last_progress_at timestamp with time zone,
            ADD COLUMN IF NOT EXISTS council_run_completed_at timestamp with time zone,
            ADD COLUMN IF NOT EXISTS council_run_failed_reason text;
        """;

    dbContext.Database.ExecuteSqlRaw(ensureColumnsSql);
    startupLogger.LogInformation("Ensured sessions.council_run_* columns exist.");
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

static string NormalizeSecretValue(string? value)
{
    if (!IsConfiguredValue(value))
    {
        return string.Empty;
    }

    return value!.Trim();
}

static string NormalizeAudience(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd('/');
}

static string ResolveClientIdFromAudience(string? audience)
{
    if (string.IsNullOrWhiteSpace(audience))
    {
        return string.Empty;
    }

    var trimmed = audience.Trim();
    if (Guid.TryParse(trimmed, out _))
    {
        return trimmed;
    }

    const string prefix = "api://";
    if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        var candidate = trimmed[prefix.Length..].Trim('/');
        return Guid.TryParse(candidate, out _) ? candidate : string.Empty;
    }

    return string.Empty;
}

static IEnumerable<string> BuildValidAudiences(string? audience, string? clientId)
{
    var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (!string.IsNullOrWhiteSpace(audience))
    {
        values.Add(audience.Trim().TrimEnd('/'));
    }
    if (!string.IsNullOrWhiteSpace(clientId))
    {
        var normalizedClientId = clientId.Trim();
        values.Add(normalizedClientId);
        values.Add($"api://{normalizedClientId}");
    }
    return values;
}

static string ResolveTenantId(string? tenantId, string? authority)
{
    if (!string.IsNullOrWhiteSpace(tenantId))
    {
        return tenantId.Trim();
    }

    if (string.IsNullOrWhiteSpace(authority))
    {
        return string.Empty;
    }

    if (!Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var authorityUri))
    {
        return string.Empty;
    }

    var firstSegment = authorityUri.AbsolutePath
        .Split('/')
        .Select(segment => segment.Trim())
        .FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment)) ?? string.Empty;

    if (string.IsNullOrWhiteSpace(firstSegment))
    {
        return string.Empty;
    }

    return firstSegment;
}

static IEnumerable<string>? BuildValidIssuers(string? authority, string? tenantId)
{
    var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (!string.IsNullOrWhiteSpace(authority))
    {
        values.Add(authority.Trim().TrimEnd('/'));
    }

    if (!string.IsNullOrWhiteSpace(tenantId))
    {
        var trimmedTenant = tenantId.Trim();
        values.Add($"https://login.microsoftonline.com/{trimmedTenant}/v2.0");
        values.Add($"https://sts.windows.net/{trimmedTenant}/");
    }

    return values.Count > 0 ? values : null;
}

static RateLimitPartition<string> BuildFixedWindowRateLimiter(
    string partitionKey,
    EndpointRateLimitConfiguration endpointConfig)
{
    var permitLimit = Math.Max(1, endpointConfig.PermitLimit);
    var windowSeconds = Math.Max(1, endpointConfig.WindowSeconds);
    var queueLimit = Math.Max(0, endpointConfig.QueueLimit);

    return RateLimitPartition.GetFixedWindowLimiter(
        partitionKey,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
}

static string ResolveRateLimitPartitionKey(HttpContext context)
{
    var userClaim =
        context.User.FindFirstValue("oid")
        ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue("sub");

    if (!string.IsNullOrWhiteSpace(userClaim))
    {
        return $"user:{userClaim.Trim()}";
    }

    var ip = context.Connection.RemoteIpAddress?.ToString();
    return string.IsNullOrWhiteSpace(ip) ? "ip:unknown" : $"ip:{ip}";
}

static string ConfigureAttachmentStorage(
    IServiceCollection services,
    IConfiguration configuration,
    StorageConfiguration storageConfiguration)
{
    var attachmentContainer = string.IsNullOrWhiteSpace(storageConfiguration.AttachmentContainer)
        ? "session-attachments"
        : storageConfiguration.AttachmentContainer.Trim();

    var blobConnectionString = ReplaceEnvVars(configuration.GetConnectionString("BlobStorage") ?? string.Empty);
    var blobServiceUriValue = ReplaceEnvVars(storageConfiguration.AttachmentBlobServiceUri);

    if (storageConfiguration.UseManagedIdentity)
    {
        if (!IsConfiguredValue(blobServiceUriValue))
        {
            throw new InvalidOperationException(
                "Storage:UseManagedIdentity is enabled but Storage:AttachmentBlobServiceUri is missing.");
        }

        if (!Uri.TryCreate(blobServiceUriValue, UriKind.Absolute, out var blobServiceUri))
        {
            throw new InvalidOperationException(
                "Storage:AttachmentBlobServiceUri must be a valid absolute URI when Storage:UseManagedIdentity is enabled.");
        }

        services.AddSingleton<IAttachmentStorageService>(_ =>
            new AzureBlobAttachmentStorageService(blobServiceUri, attachmentContainer, CreateDefaultAzureCredential()));

        return "managed-identity";
    }

    if (IsConfiguredValue(blobConnectionString))
    {
        services.AddSingleton<IAttachmentStorageService>(_ =>
            new AzureBlobAttachmentStorageService(blobConnectionString, attachmentContainer));
        return "connection-string";
    }

    if (IsConfiguredValue(blobServiceUriValue))
    {
        throw new InvalidOperationException(
            "Storage:AttachmentBlobServiceUri is configured but Storage:UseManagedIdentity is false. Enable managed identity or configure ConnectionStrings:BlobStorage.");
    }

    return "disabled";
}

static void RegisterPersistenceServices(IServiceCollection services)
{
    services.AddScoped<ISessionRepository, SessionRepository>();
    services.AddScoped<ITruthMapRepository, TruthMapRepository>();
    services.AddScoped<IAgentMessageRepository, AgentMessageRepository>();
    services.AddScoped<ITokenUsageRepository, TokenUsageRepository>();
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

static DefaultAzureCredential CreateDefaultAzureCredential() => new(new DefaultAzureCredentialOptions
{
    ExcludeInteractiveBrowserCredential = true
});

static NpgsqlDataSource CreatePostgresManagedIdentityDataSource(string connectionString)
{
    var credential = CreateDefaultAzureCredential();
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

    var credential = CreateDefaultAzureCredential();

    options.ConfigureForAzureWithTokenCredentialAsync(credential).GetAwaiter().GetResult();
    return ConnectionMultiplexer.Connect(options);
}

static string[] ResolveAllowedCorsOrigins(IConfiguration configuration)
{
    var origins = new List<string>();

    var sectionOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (sectionOrigins is not null)
    {
        foreach (var origin in sectionOrigins)
        {
            if (!string.IsNullOrWhiteSpace(origin))
            {
                origins.Add(origin.Trim());
            }
        }
    }

    var delimitedOrigins = configuration["Cors:AllowedOrigins"];
    if (!string.IsNullOrWhiteSpace(delimitedOrigins))
    {
        var parsed = delimitedOrigins.Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        origins.AddRange(parsed);
    }

    return origins
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static void ValidateTrialAccessAccessMode(string? rawValue, bool isNonProductionSafeEnvironment)
{
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        return;
    }

    if (Enum.TryParse<TrialAccessMode>(rawValue.Trim(), ignoreCase: true, out _))
    {
        return;
    }

    var message =
        $"SECURITY: Invalid TrialAccess:AccessMode '{rawValue}'. " +
        $"Allowed values: {TrialAccessMode.RestrictedGroups}, {TrialAccessMode.AllAuthenticatedUsers}.";

    if (!isNonProductionSafeEnvironment)
    {
        throw new InvalidOperationException(message);
    }

    Console.WriteLine($"WARNING: {message} Falling back to default mode.");
}

// Make Program class accessible to integration tests
public partial class Program { }
