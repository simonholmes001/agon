using Agon.Api.Configuration;
using Agon.Api.Middleware;
using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Services;
using Agon.Domain.Agents;
using Agon.Infrastructure.Agents;
using Agon.Infrastructure.Persistence.PostgreSQL;
using Agon.Infrastructure.Persistence.Redis;
using Agon.Infrastructure.SignalR;
using Anthropic;
using Google.GenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Mscc.GenerativeAI.Microsoft;
using OpenAI;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Load Environment Variables from .env File ───────────────────────────
// This allows us to read OPENAI_KEY, CLAUDE_KEY, etc. from the .env file
var envFilePath = Path.Combine(Directory.GetParent(builder.Environment.ContentRootPath)?.Parent?.FullName ?? "", ".env");
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
        }
    }
}

// ── Configuration ───────────────────────────────────────────────────────
var llmConfig = builder.Configuration.GetSection(LlmConfiguration.SectionName).Get<LlmConfiguration>() ?? new();
var agonConfig = builder.Configuration.GetSection(AgonConfiguration.SectionName).Get<AgonConfiguration>() ?? new();

// Replace ${ENV_VAR} placeholders with actual environment variables
llmConfig.OpenAI.ApiKey = ReplaceEnvVars(llmConfig.OpenAI.ApiKey);
llmConfig.Anthropic.ApiKey = ReplaceEnvVars(llmConfig.Anthropic.ApiKey);
llmConfig.Google.ApiKey = ReplaceEnvVars(llmConfig.Google.ApiKey);
llmConfig.DeepSeek.ApiKey = ReplaceEnvVars(llmConfig.DeepSeek.ApiKey);

builder.Services.AddSingleton(llmConfig);
builder.Services.AddSingleton(agonConfig);

// ── Controllers and OpenAPI ─────────────────────────────────────────────
builder.Services.AddControllers();
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
builder.Services.AddScoped<ISessionService, SessionService>();

// Only register Orchestrator and AgentRunner if dependencies are available
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddScoped<IAgentRunner, AgentRunner>();
    builder.Services.AddScoped<Orchestrator>();
}

// ── Infrastructure: Council Agents (MAF with Multiple Providers) ────────
if (!builder.Environment.IsEnvironment("Testing"))
{
    // Register AgentResponseParser adapter (shared by all agents)
    builder.Services.AddScoped<IAgentResponseParser, AgentResponseParserAdapter>();

    var config = builder.Configuration.Get<LlmConfiguration>() ?? throw new InvalidOperationException("LlmConfiguration is missing");

    // ── Register Council Agents with Provider-Specific Native MAF AIAgents ──
    
    // Moderator: OpenAI GPT (via ChatCompletion API)
    builder.Services.AddScoped<ICouncilAgent>(sp =>
    {
        var parser = sp.GetRequiredService<IAgentResponseParser>();
        // Cast ChatClient to IChatClient for .AsAIAgent() extension
        var aiAgent = ((IChatClient)new OpenAIClient(config.OpenAI.ApiKey)
            .GetChatClient(config.OpenAI.Model))
            .AsAIAgent(
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
        var parser = sp.GetRequiredService<IAgentResponseParser>();
        // Cast ChatClient to IChatClient for .AsAIAgent() extension
        var aiAgent = ((IChatClient)new OpenAIClient(config.OpenAI.ApiKey)
            .GetChatClient(config.OpenAI.Model))
            .AsAIAgent(
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
        var parser = sp.GetRequiredService<IAgentResponseParser>();
        // Cast ChatClient to IChatClient for .AsAIAgent() extension
        var aiAgent = ((IChatClient)new OpenAIClient(config.OpenAI.ApiKey)
            .GetChatClient(config.OpenAI.Model))
            .AsAIAgent(
                instructions: AgentSystemPrompts.Synthesizer,
                name: AgentId.Synthesizer);
        
        return new MafCouncilAgent(
            agentId: AgentId.Synthesizer,
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();

// ── Map Endpoints ───────────────────────────────────────────────────────
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
