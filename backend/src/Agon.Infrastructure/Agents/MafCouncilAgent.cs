using System.Diagnostics;
using System.Runtime.CompilerServices;
using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Agon.Infrastructure.Agents;

/// <summary>
/// Provider-agnostic council agent backed by <see cref="IChatClient"/> from Microsoft.Extensions.AI.
/// All provider differences (OpenAI, Anthropic, Gemini, DeepSeek) are handled by the
/// <see cref="IChatClient"/> implementation injected at DI registration time.
/// This class contains zero provider-specific code.
/// </summary>
public sealed class MafCouncilAgent(
    string agentId,
    string modelProvider,
    IChatClient chatClient,
    int maxOutputTokens,
    ILogger<MafCouncilAgent> logger) : ICouncilAgent
{
    public string AgentId { get; } = agentId;
    public string ModelProvider { get; } = modelProvider;

    public async Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        var prompt = AgentPromptComposer.ComposePrompt(agentId, context);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };
        var options = new ChatOptions { MaxOutputTokens = maxOutputTokens };

        try
        {
            var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
            var text = response.Text ?? string.Empty;
            var parsed = AgentResponseParser.Parse(text);

            logger.LogInformation(
                "Agent call succeeded. SessionId={SessionId} Round={Round} AgentId={AgentId} Provider={Provider} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
                context.SessionId,
                context.Round,
                agentId,
                modelProvider,
                startedAt.ElapsedMilliseconds,
                context.CorrelationId);

            return new AgentResponse
            {
                Message = parsed.Message,
                Patch = parsed.Patch,
                RawOutput = text
            };
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Agent call failed. SessionId={SessionId} Round={Round} AgentId={AgentId} Provider={Provider} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
                context.SessionId,
                context.Round,
                agentId,
                modelProvider,
                startedAt.ElapsedMilliseconds,
                context.CorrelationId);
            throw;
        }
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(
        AgentContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        var prompt = AgentPromptComposer.ComposePrompt(agentId, context);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };
        var options = new ChatOptions { MaxOutputTokens = maxOutputTokens };

        var succeeded = false;

        try
        {
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                var token = update.Text ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    yield return token;
                }
            }

            succeeded = true;
        }
        finally
        {
            if (succeeded)
            {
                logger.LogInformation(
                    "Agent streaming call succeeded. SessionId={SessionId} Round={Round} AgentId={AgentId} Provider={Provider} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
                    context.SessionId,
                    context.Round,
                    agentId,
                    modelProvider,
                    startedAt.ElapsedMilliseconds,
                    context.CorrelationId);
            }
            else
            {
                logger.LogError(
                    "Agent streaming call failed. SessionId={SessionId} Round={Round} AgentId={AgentId} Provider={Provider} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
                    context.SessionId,
                    context.Round,
                    agentId,
                    modelProvider,
                    startedAt.ElapsedMilliseconds,
                    context.CorrelationId);
            }
        }
    }
}
