using Agon.Api.Configuration;
using Agon.Application.Interfaces;
using Agon.Domain.Agents;
using Agon.Infrastructure.Agents;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agon.Api.Tests.Configuration;

public class AgentRegistrationExtensionsTests
{
    #region Full Valid Configuration

    [Fact]
    public void AddCouncilAgents_WithAllApiKeysConfigured_RegistersFiveAgents()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: "test-openai-key",
            geminiKey: "test-gemini-key",
            anthropicKey: "test-anthropic-key");

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();
        agents.Should().HaveCount(5);
    }

    [Fact]
    public void AddCouncilAgents_WithAllApiKeysConfigured_RegistersAllExpectedAgentIds()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: "test-openai-key",
            geminiKey: "test-gemini-key",
            anthropicKey: "test-anthropic-key");

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();
        agents.Select(a => a.AgentId).Should().BeEquivalentTo(
            new[] { AgentId.Moderator, AgentId.GptAgent, AgentId.GeminiAgent, AgentId.ClaudeAgent, AgentId.Synthesizer });
    }

    [Fact]
    public void AddCouncilAgents_WithAllApiKeysConfigured_RegistersCorrectAgentTypes()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: "test-openai-key",
            geminiKey: "test-gemini-key",
            anthropicKey: "test-anthropic-key");

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();

        // OpenAI agents (Moderator, GptAgent, Synthesizer)
        agents.Count(a => a is OpenAiCouncilAgent).Should().Be(3);

        // Gemini agent
        agents.Count(a => a is GeminiCouncilAgent).Should().Be(1);

        // Anthropic agent
        agents.Count(a => a is AnthropicCouncilAgent).Should().Be(1);
    }

    #endregion

    #region Missing OpenAI API Key

    [Fact]
    public void AddCouncilAgents_WithMissingOpenAiKey_RegistersErrorAgentForModerator()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: null,
            geminiKey: "test-gemini-key",
            anthropicKey: "test-anthropic-key");

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();
        var moderator = agents.Single(a => a.AgentId == AgentId.Moderator);

        moderator.Should().BeOfType<ConfigurationErrorCouncilAgent>();
    }

    [Fact]
    public void AddCouncilAgents_WithMissingOpenAiKey_RegistersErrorAgentForGptAgent()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: null,
            geminiKey: "test-gemini-key",
            anthropicKey: "test-anthropic-key");

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();
        var gptAgent = agents.Single(a => a.AgentId == AgentId.GptAgent);

        gptAgent.Should().BeOfType<ConfigurationErrorCouncilAgent>();
    }

    [Fact]
    public void AddCouncilAgents_WithMissingOpenAiKey_RegistersErrorAgentForSynthesizer()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: null,
            geminiKey: "test-gemini-key",
            anthropicKey: "test-anthropic-key");

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();
        var synthesizer = agents.Single(a => a.AgentId == AgentId.Synthesizer);

        synthesizer.Should().BeOfType<ConfigurationErrorCouncilAgent>();
    }

    [Fact]
    public void AddCouncilAgents_WithMissingOpenAiKey_StillRegistersGeminiAgent()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: null,
            geminiKey: "test-gemini-key",
            anthropicKey: "test-anthropic-key");

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();
        var geminiAgent = agents.Single(a => a.AgentId == AgentId.GeminiAgent);

        geminiAgent.Should().BeOfType<GeminiCouncilAgent>();
    }

    #endregion

    #region Missing Gemini API Key

    [Fact]
    public void AddCouncilAgents_WithMissingGeminiKey_RegistersErrorAgentForGeminiAgent()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: "test-openai-key",
            geminiKey: null,
            anthropicKey: "test-anthropic-key");

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();
        var geminiAgent = agents.Single(a => a.AgentId == AgentId.GeminiAgent);

        geminiAgent.Should().BeOfType<ConfigurationErrorCouncilAgent>();
    }

    [Fact]
    public void AddCouncilAgents_WithMissingGeminiKey_StillRegistersOpenAiAgents()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: "test-openai-key",
            geminiKey: null,
            anthropicKey: "test-anthropic-key");

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();

        agents.Single(a => a.AgentId == AgentId.Moderator).Should().BeOfType<OpenAiCouncilAgent>();
        agents.Single(a => a.AgentId == AgentId.GptAgent).Should().BeOfType<OpenAiCouncilAgent>();
        agents.Single(a => a.AgentId == AgentId.Synthesizer).Should().BeOfType<OpenAiCouncilAgent>();
    }

    #endregion

    #region Missing Anthropic API Key

    [Fact]
    public void AddCouncilAgents_WithMissingAnthropicKey_RegistersErrorAgentForClaudeAgent()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: "test-openai-key",
            geminiKey: "test-gemini-key",
            anthropicKey: null);

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();
        var claudeAgent = agents.Single(a => a.AgentId == AgentId.ClaudeAgent);

        claudeAgent.Should().BeOfType<ConfigurationErrorCouncilAgent>();
    }

    [Fact]
    public void AddCouncilAgents_WithMissingAnthropicKey_StillRegistersOtherAgents()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: "test-openai-key",
            geminiKey: "test-gemini-key",
            anthropicKey: null);

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();

        agents.Single(a => a.AgentId == AgentId.GeminiAgent).Should().BeOfType<GeminiCouncilAgent>();
        agents.Single(a => a.AgentId == AgentId.Moderator).Should().BeOfType<OpenAiCouncilAgent>();
    }

    #endregion

    #region All Keys Missing

    [Fact]
    public void AddCouncilAgents_WithNoApiKeys_RegistersAllAsConfigurationErrorAgents()
    {
        var services = CreateServicesWithLogging();
        var config = CreateProviderConfiguration(
            openAiKey: null,
            geminiKey: null,
            anthropicKey: null);

        services.AddCouncilAgents(config);
        var provider = services.BuildServiceProvider();

        var agents = provider.GetServices<ICouncilAgent>().ToList();

        agents.Should().HaveCount(5);
        agents.Should().AllSatisfy(agent => agent.Should().BeOfType<ConfigurationErrorCouncilAgent>());
    }

    #endregion

    #region Helper Methods

    private static IServiceCollection CreateServicesWithLogging()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddHttpClient();
        return services;
    }

    private static ProviderConfiguration CreateProviderConfiguration(
        string? openAiKey,
        string? geminiKey,
        string? anthropicKey)
    {
        var configValues = new Dictionary<string, string?>();

        if (openAiKey is not null)
        {
            configValues["OpenAI:ApiKey"] = openAiKey;
        }

        if (geminiKey is not null)
        {
            configValues["Gemini:ApiKey"] = geminiKey;
        }

        if (anthropicKey is not null)
        {
            configValues["Anthropic:ApiKey"] = anthropicKey;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        return ProviderConfiguration.Load(configuration);
    }

    #endregion
}
