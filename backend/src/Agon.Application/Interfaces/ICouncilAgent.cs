using Microsoft.Agents.AI;
using AgonAgentResponse = Agon.Application.Models.AgentResponse;
using AgonAgentContext = Agon.Application.Models.AgentContext;

namespace Agon.Application.Interfaces;

/// <summary>
/// Abstraction over a single council agent backed by MAF's AIAgent.
/// This is a thin wrapper that adds our application-specific context (AgentContext)
/// and response parsing (AgentResponse) on top of MAF's native AIAgent.
/// </summary>
public interface ICouncilAgent
{
    /// <summary>Canonical agent identifier — matches <see cref="Agon.Domain.Agents.AgentId"/> constants.</summary>
    string AgentId { get; }

    /// <summary>Human-readable model/provider label (e.g., "OpenAI gpt-4o", "Anthropic claude-opus-4").</summary>
    string ModelProvider { get; }

    /// <summary>The underlying MAF AIAgent instance.</summary>
    AIAgent UnderlyingAgent { get; }

    /// <summary>
    /// Runs the agent for a single round and returns the complete MESSAGE + PATCH response.
    /// This method is called by <see cref="Agon.Application.Orchestration.AgentRunner"/>
    /// inside a <c>Task.WhenAll</c> for parallel phases.
    /// </summary>
    Task<AgonAgentResponse> RunAsync(AgonAgentContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Streams agent output tokens in real time.
    /// Used by the Infrastructure layer to fan out tokens to SignalR clients.
    /// The caller is responsible for accumulating the full response separately.
    /// </summary>
    IAsyncEnumerable<string> RunStreamingAsync(AgonAgentContext context, CancellationToken cancellationToken);
}
