using Agon.Application.Interfaces;
using Agon.Domain.TruthMap;

namespace Agon.Application.Orchestration;

public class AgentRunner(ITruthMapRepository truthMapRepository)
{
    public async Task<IReadOnlyList<AgentExecutionResult>> RunRoundAsync(
        IEnumerable<ICouncilAgent> agents,
        AgentContext context,
        TimeSpan timeoutPerAgent,
        CancellationToken cancellationToken)
    {
        var tasks = agents.Select(agent => ExecuteAgentAsync(agent, context, timeoutPerAgent, cancellationToken));
        return await Task.WhenAll(tasks);
    }

    public async Task ApplyValidatedPatchesAsync(
        Guid sessionId,
        IEnumerable<AgentExecutionResult> results,
        CancellationToken cancellationToken)
    {
        foreach (var result in results
                     .Where(result => !result.TimedOut && result.Patch is not null)
                     .OrderBy(result => result.AgentId, StringComparer.Ordinal))
        {
            await truthMapRepository.ApplyPatchAsync(sessionId, result.Patch!, cancellationToken);
        }
    }

    private static async Task<AgentExecutionResult> ExecuteAgentAsync(
        ICouncilAgent agent,
        AgentContext context,
        TimeSpan timeoutPerAgent,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runTask = agent.RunAsync(context, timeoutCts.Token);
        var timeoutTask = Task.Delay(timeoutPerAgent, cancellationToken);

        var completed = await Task.WhenAny(runTask, timeoutTask);
        if (completed != runTask)
        {
            timeoutCts.Cancel();
            return AgentExecutionResult.Timeout(agent.AgentId);
        }

        try
        {
            var response = await runTask;
            return AgentExecutionResult.Success(agent.AgentId, response.Patch, response.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AgentExecutionResult.Timeout(agent.AgentId);
        }
        catch (Exception exception)
        {
            return AgentExecutionResult.Failed(agent.AgentId, exception.Message);
        }
    }
}
