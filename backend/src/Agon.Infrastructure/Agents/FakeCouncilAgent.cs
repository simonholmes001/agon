using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Domain.TruthMap;

namespace Agon.Infrastructure.Agents;

public class FakeCouncilAgent(
    string agentId,
    string modelProvider,
    string message,
    TruthMapPatch? patch = null) : ICouncilAgent
{
    public string AgentId { get; } = agentId;
    public string ModelProvider { get; } = modelProvider;

    public Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AgentResponse
        {
            Message = message,
            Patch = patch,
            RawOutput = message
        });
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(
        AgentContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return message;
    }
}
