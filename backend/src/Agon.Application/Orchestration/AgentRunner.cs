using Agon.Application.Interfaces;
using Agon.Domain.TruthMap;
using Microsoft.Extensions.Logging;

namespace Agon.Application.Orchestration;

public class AgentRunner(
    ITruthMapRepository truthMapRepository,
    IEventBroadcaster eventBroadcaster,
    ILogger<AgentRunner> logger)
{
    public async Task<IReadOnlyList<AgentExecutionResult>> RunRoundAsync(
        IEnumerable<ICouncilAgent> agents,
        AgentContext context,
        TimeSpan timeoutPerAgent,
        CancellationToken cancellationToken)
    {
        var agentList = agents.ToList();
        logger.LogInformation(
            "Running agent round. SessionId={SessionId} Round={Round} Phase={Phase} AgentCount={AgentCount} CorrelationId={CorrelationId}",
            context.SessionId,
            context.Round,
            context.Phase,
            agentList.Count,
            context.CorrelationId);

        var tasks = agentList.Select(agent => ExecuteAgentAsync(agent, context, timeoutPerAgent, cancellationToken));
        var results = await Task.WhenAll(tasks);

        logger.LogInformation(
            "Completed agent round. SessionId={SessionId} Round={Round} TimedOut={TimedOutCount} Failed={FailedCount} CorrelationId={CorrelationId}",
            context.SessionId,
            context.Round,
            results.Count(result => result.TimedOut),
            results.Count(result => result.Error is not null && result.Error != "timeout"),
            context.CorrelationId);

        return results;
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
            var updatedMap = await truthMapRepository.GetAsync(sessionId, cancellationToken);
            var version = updatedMap?.Version ?? 0;
            await eventBroadcaster.TruthMapPatchedAsync(sessionId, result.Patch!, version, cancellationToken);
            logger.LogInformation(
                "Applied validated patch. SessionId={SessionId} Agent={Agent} Version={Version}",
                sessionId,
                result.AgentId,
                version);
        }
    }

    private async Task<AgentExecutionResult> ExecuteAgentAsync(
        ICouncilAgent agent,
        AgentContext context,
        TimeSpan timeoutPerAgent,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<AgentResponse> runTask;
        try
        {
            runTask = agent.RunAsync(context, timeoutCts.Token);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Agent failed synchronously before task creation. SessionId={SessionId} Round={Round} AgentId={AgentId} CorrelationId={CorrelationId}",
                context.SessionId,
                context.Round,
                agent.AgentId,
                context.CorrelationId);
            return AgentExecutionResult.Failed(agent.AgentId, exception.Message);
        }

        var timeoutTask = Task.Delay(timeoutPerAgent, cancellationToken);

        var completed = await Task.WhenAny(runTask, timeoutTask);
        if (completed != runTask)
        {
            timeoutCts.Cancel();
            logger.LogWarning(
                "Agent timed out. SessionId={SessionId} Round={Round} AgentId={AgentId} TimeoutMs={TimeoutMs} CorrelationId={CorrelationId}",
                context.SessionId,
                context.Round,
                agent.AgentId,
                timeoutPerAgent.TotalMilliseconds,
                context.CorrelationId);
            return AgentExecutionResult.Timeout(agent.AgentId);
        }

        try
        {
            var response = await runTask;
            logger.LogInformation(
                "Agent completed. SessionId={SessionId} Round={Round} AgentId={AgentId} HasPatch={HasPatch} CorrelationId={CorrelationId}",
                context.SessionId,
                context.Round,
                agent.AgentId,
                response.Patch is not null,
                context.CorrelationId);
            return AgentExecutionResult.Success(agent.AgentId, response.Patch, response.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Agent canceled due to timeout. SessionId={SessionId} Round={Round} AgentId={AgentId} CorrelationId={CorrelationId}",
                context.SessionId,
                context.Round,
                agent.AgentId,
                context.CorrelationId);
            return AgentExecutionResult.Timeout(agent.AgentId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Agent failed. SessionId={SessionId} Round={Round} AgentId={AgentId} CorrelationId={CorrelationId}",
                context.SessionId,
                context.Round,
                agent.AgentId,
                context.CorrelationId);
            return AgentExecutionResult.Failed(agent.AgentId, exception.Message);
        }
    }
}
