using Agon.Application.Models;
using Agon.Domain.Sessions;

namespace Agon.Application.Orchestration;

/// <summary>
/// Abstraction over <see cref="AgentRunner"/> to allow test isolation.
/// </summary>
public interface IAgentRunner
{
    Task<AgentResponse> RunModeratorAsync(
        SessionState state, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentResponse>> RunAnalysisRoundAsync(
        SessionState state, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentResponse>> RunCritiqueRoundAsync(
        SessionState state, CancellationToken cancellationToken);

    Task<AgentResponse> RunSynthesisAsync(
        SessionState state, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentResponse>> RunTargetedLoopAsync(
        SessionState state,
        IReadOnlyList<string> targetAgentIds,
        string microDirective,
        CancellationToken cancellationToken);

    Task<AgentResponse> RunPostDeliveryFollowUpAsync(
        SessionState state,
        string userMessage,
        CancellationToken cancellationToken);
}
