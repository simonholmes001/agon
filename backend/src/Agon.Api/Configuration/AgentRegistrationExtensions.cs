using Agon.Application.Interfaces;
using Agon.Domain.Agents;
using Agon.Infrastructure.Agents;
using GenerativeAI.Microsoft;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Agon.Api.Configuration;

/// <summary>
/// Extension methods for registering council agents in the DI container.
/// Each agent is backed by a provider-specific <see cref="IChatClient"/> from
/// Microsoft.Extensions.AI. <see cref="MafCouncilAgent"/> is provider-agnostic —
/// all provider differences are encapsulated in the injected <see cref="IChatClient"/>.
/// </summary>
public static class AgentRegistrationExtensions
{
    private const string ProviderOpenAi = "openai";
    private const string ProviderGemini = "gemini";
    private const string ProviderAnthropic = "anthropic";

    /// <summary>
    /// Registers all council agents (Moderator, GptAgent, GeminiAgent, ClaudeAgent, CritiqueAgent, Synthesizer).
    /// </summary>
    public static IServiceCollection AddCouncilAgents(
        this IServiceCollection services,
        ProviderConfiguration config)
    {
        // 1. Moderator — OpenAI GPT-5.2 — Clarification phase
        services.AddSingleton<ICouncilAgent>(sp =>
            CreateAgent(sp, config, AgentId.Moderator, ProviderOpenAi, config.OpenAiMaxOutputTokens,
                () => BuildOpenAiChatClient(config.OpenAiApiKey!, config.OpenAiModel)));

        // 2. GptAgent — OpenAI GPT-5.2 — Construction + Refinement phases
        services.AddSingleton<ICouncilAgent>(sp =>
            CreateAgent(sp, config, AgentId.GptAgent, ProviderOpenAi, config.TechnicalArchitectMaxOutputTokens,
                () => BuildOpenAiChatClient(config.OpenAiApiKey!, config.OpenAiModel)));

        // 3. GeminiAgent — Google Gemini 3 — Construction + Refinement phases
        services.AddSingleton<ICouncilAgent>(sp =>
            CreateAgent(sp, config, AgentId.GeminiAgent, ProviderGemini, config.GeminiMaxOutputTokens,
                () => BuildGeminiChatClient(config.GeminiApiKey!, config.GeminiModel)));

        // 4. ClaudeAgent — Anthropic Claude Opus 4.6 — Construction + Refinement phases
        services.AddSingleton<ICouncilAgent>(sp =>
            CreateAgent(sp, config, AgentId.ClaudeAgent, ProviderAnthropic, config.AnthropicMaxOutputTokens,
                () => BuildAnthropicChatClient(config.AnthropicApiKey!, config.AnthropicModel)));

        // 5. CritiqueAgent — Google Gemini 3 — Critique phase
        services.AddSingleton<ICouncilAgent>(sp =>
            CreateAgent(sp, config, AgentId.CritiqueAgent, ProviderGemini, config.GeminiMaxOutputTokens,
                () => BuildGeminiChatClient(config.GeminiApiKey!, config.GeminiModel)));

        // 6. Synthesizer — OpenAI GPT-5.2 — Synthesis + TargetedLoop phases
        services.AddSingleton<ICouncilAgent>(sp =>
            CreateAgent(sp, config, AgentId.Synthesizer, ProviderOpenAi, config.SynthesisMaxOutputTokens,
                () => BuildOpenAiChatClient(config.OpenAiApiKey!, config.OpenAiModel)));

        return services;
    }

    // -----------------------------------------------------------------------
    // Provider IChatClient factories — one per provider, zero bespoke HTTP code
    // -----------------------------------------------------------------------

    private static IChatClient BuildOpenAiChatClient(string apiKey, string modelName) =>
        new OpenAIClient(apiKey)
            .GetChatClient(modelName)
            .AsIChatClient();

    private static IChatClient BuildGeminiChatClient(string apiKey, string modelName) =>
        new GenerativeAIChatClient(apiKey, modelName);

    private static IChatClient BuildAnthropicChatClient(string apiKey, string modelName) =>
        new Anthropic.AnthropicClient { ApiKey = apiKey }
            .AsIChatClient(modelName);

    // -----------------------------------------------------------------------
    // Agent factory — falls back to ConfigurationErrorCouncilAgent when key missing
    // -----------------------------------------------------------------------

    private static ICouncilAgent CreateAgent(
        IServiceProvider sp,
        ProviderConfiguration config,
        string agentId,
        string provider,
        int maxOutputTokens,
        Func<IChatClient> chatClientFactory)
    {
        var apiKey = provider switch
        {
            ProviderOpenAi => config.OpenAiApiKey,
            ProviderGemini => config.GeminiApiKey,
            ProviderAnthropic => config.AnthropicApiKey,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ConfigurationErrorCouncilAgent(
                agentId,
                modelProvider: provider,
                errorMessage: $"Missing API key for provider '{provider}' (agent '{agentId}').",
                logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
        }

        return new MafCouncilAgent(
            agentId,
            provider,
            chatClientFactory(),
            maxOutputTokens,
            sp.GetRequiredService<ILogger<MafCouncilAgent>>());
    }
}
