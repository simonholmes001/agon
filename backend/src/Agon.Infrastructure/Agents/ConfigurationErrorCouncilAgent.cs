using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Microsoft.Extensions.Logging;

namespace Agon.Infrastructure.Agents;

public class ConfigurationErrorCouncilAgent(
    string agentId,
    string modelProvider,
    string errorMessage,
    ILogger<ConfigurationErrorCouncilAgent> logger) : ICouncilAgent
{
    public string AgentId { get; } = agentId;
    public string ModelProvider { get; } = modelProvider;

    public Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        logger.LogError(
            "Agent configuration error. SessionId={SessionId} Round={Round} AgentId={AgentId} Provider={Provider} Error={Error}",
            context.SessionId,
            context.Round,
            AgentId,
            ModelProvider,
            errorMessage);

        return Task.FromException<AgentResponse>(new InvalidOperationException(errorMessage));
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(
        AgentContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw new InvalidOperationException(errorMessage);
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
