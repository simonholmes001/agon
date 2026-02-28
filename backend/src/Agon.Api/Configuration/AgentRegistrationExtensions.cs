using Agon.Application.Interfaces;
using Agon.Domain.Agents;
using Agon.Infrastructure.Agents;

namespace Agon.Api.Configuration;

/// <summary>
/// Extension methods for registering council agents in the DI container.
/// </summary>
public static class AgentRegistrationExtensions
{
    /// <summary>
    /// Registers all council agents (Moderator, GptAgent, GeminiAgent, ClaudeAgent, Synthesizer).
    /// </summary>
    public static IServiceCollection AddCouncilAgents(
        this IServiceCollection services,
        ProviderConfiguration config)
    {
        // 1. Moderator (OpenAI GPT-5.2) - Clarification phase
        services.AddSingleton<ICouncilAgent>(sp => CreateModeratorAgent(sp, config));

        // 2. GptAgent (OpenAI GPT-5.2) - DraftRound1 + Critique phases
        services.AddSingleton<ICouncilAgent>(sp => CreateGptAgent(sp, config));

        // 3. GeminiAgent (Gemini 3.1 Pro) - DraftRound2 + Critique phases
        services.AddSingleton<ICouncilAgent>(sp => CreateGeminiAgent(sp, config));

        // 4. ClaudeAgent (Anthropic Claude Opus 4.6) - DraftRound3 + Critique phases
        services.AddSingleton<ICouncilAgent>(sp => CreateClaudeAgent(sp, config));

        // 5. Synthesizer (OpenAI GPT-5.2) - Synthesis + TargetedLoop phases
        services.AddSingleton<ICouncilAgent>(sp => CreateSynthesizerAgent(sp, config));

        return services;
    }

    private static ICouncilAgent CreateModeratorAgent(IServiceProvider sp, ProviderConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
        {
            return new ConfigurationErrorCouncilAgent(
                AgentId.Moderator,
                modelProvider: "openai",
                errorMessage: "Missing OPENAI_KEY for agent 'moderator'.",
                logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
        }

        return new OpenAiCouncilAgent(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
            new OpenAiCouncilAgentOptions(
                AgentId.Moderator,
                config.OpenAiApiKey,
                config.OpenAiModel,
                MaxOutputTokens: config.OpenAiMaxOutputTokens,
                Temperature: config.OpenAiTemperature),
            sp.GetRequiredService<ILogger<OpenAiCouncilAgent>>());
    }

    private static ICouncilAgent CreateGptAgent(IServiceProvider sp, ProviderConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
        {
            return new ConfigurationErrorCouncilAgent(
                AgentId.GptAgent,
                modelProvider: "openai",
                errorMessage: "Missing OPENAI_KEY for agent 'gpt_agent'.",
                logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
        }

        return new OpenAiCouncilAgent(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
            new OpenAiCouncilAgentOptions(
                AgentId.GptAgent,
                config.OpenAiApiKey,
                config.OpenAiModel,
                MaxOutputTokens: config.TechnicalArchitectMaxOutputTokens,
                Temperature: config.TechnicalArchitectTemperature),
            sp.GetRequiredService<ILogger<OpenAiCouncilAgent>>());
    }

    private static ICouncilAgent CreateGeminiAgent(IServiceProvider sp, ProviderConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.GeminiApiKey))
        {
            return new ConfigurationErrorCouncilAgent(
                AgentId.GeminiAgent,
                modelProvider: "gemini",
                errorMessage: "Missing GEMINI_KEY for agent 'gemini_agent'.",
                logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
        }

        return new GeminiCouncilAgent(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
            new GeminiCouncilAgentOptions(
                AgentId.GeminiAgent,
                config.GeminiApiKey,
                config.GeminiModel,
                MaxOutputTokens: config.GeminiMaxOutputTokens,
                Temperature: config.GeminiTemperature),
            sp.GetRequiredService<ILogger<GeminiCouncilAgent>>());
    }

    private static ICouncilAgent CreateClaudeAgent(IServiceProvider sp, ProviderConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.AnthropicApiKey))
        {
            return new ConfigurationErrorCouncilAgent(
                AgentId.ClaudeAgent,
                modelProvider: "anthropic",
                errorMessage: "Missing ANTHROPIC_KEY or CLAUDE_KEY for agent 'claude_agent'.",
                logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
        }

        return new AnthropicCouncilAgent(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
            new AnthropicCouncilAgentOptions(
                AgentId.ClaudeAgent,
                config.AnthropicApiKey,
                config.AnthropicModel,
                MaxOutputTokens: config.AnthropicMaxOutputTokens,
                Temperature: config.AnthropicTemperature),
            sp.GetRequiredService<ILogger<AnthropicCouncilAgent>>());
    }

    private static ICouncilAgent CreateSynthesizerAgent(IServiceProvider sp, ProviderConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
        {
            return new ConfigurationErrorCouncilAgent(
                AgentId.Synthesizer,
                modelProvider: "openai",
                errorMessage: "Missing OPENAI_KEY for agent 'synthesizer'.",
                logger: sp.GetRequiredService<ILogger<ConfigurationErrorCouncilAgent>>());
        }

        return new OpenAiCouncilAgent(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
            new OpenAiCouncilAgentOptions(
                AgentId.Synthesizer,
                config.OpenAiApiKey,
                config.OpenAiModel,
                MaxOutputTokens: config.SynthesisMaxOutputTokens,
                Temperature: config.OpenAiTemperature),
            sp.GetRequiredService<ILogger<OpenAiCouncilAgent>>());
    }
}
