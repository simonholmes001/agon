using Agon.Application.Orchestration;

namespace Agon.Application.Interfaces;

public interface ICouncilAgent
{
    string AgentId { get; }
    string ModelProvider { get; }
    Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken);
    IAsyncEnumerable<string> RunStreamingAsync(AgentContext context, CancellationToken cancellationToken);
}
