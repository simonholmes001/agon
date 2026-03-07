using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Microsoft.Extensions.Logging;

namespace Agon.Application.Orchestration;

/// <summary>
/// Dispatches agent calls for each session phase.
///
/// Concurrency strategy:
/// - Analysis Round and Critique: all agents called concurrently via <c>Task.WhenAll</c>.
/// - Patches applied sequentially in alphabetical agent-ID order after all tasks complete.
/// - Per-agent timeout via a linked <c>CancellationTokenSource</c>.
/// - Token budget tracked on the <see cref="SessionState"/> after each response.
/// </summary>
public sealed class AgentRunner : IAgentRunner
{
    private readonly IReadOnlyList<ICouncilAgent> _agents;
    private readonly ITruthMapRepository _truthMapRepository;
    private readonly IEventBroadcaster _broadcaster;
    private readonly int _agentTimeoutSeconds;
    private readonly ILogger<AgentRunner>? _logger;

    public AgentRunner(
        IReadOnlyList<ICouncilAgent> agents,
        ITruthMapRepository truthMapRepository,
        IEventBroadcaster broadcaster,
        int agentTimeoutSeconds = 90,
        ILogger<AgentRunner>? logger = null)
    {
        _agents = agents;
        _truthMapRepository = truthMapRepository;
        _broadcaster = broadcaster;
        _agentTimeoutSeconds = agentTimeoutSeconds;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Dispatches all council agents in parallel for the Analysis Round.
    /// Applies valid patches sequentially (alphabetical order) then updates token budget.
    /// </summary>
    public async Task<IReadOnlyList<AgentResponse>> RunAnalysisRoundAsync(
        SessionState state,
        CancellationToken cancellationToken)
    {
        var contexts = _agents.Select(a =>
            AgentContext.ForAnalysis(
                state.SessionId,
                state.TruthMap,
                state.FrictionLevel,
                state.CurrentRound,
                state.ResearchToolsEnabled))
            .ToList();

        var responses = await DispatchParallelAsync(_agents, contexts, cancellationToken);
        await ApplyPatchesAsync(state, responses, cancellationToken);
        AccumulateTokens(state, responses);
        return responses;
    }

    /// <summary>
    /// Dispatches all council agents in parallel for the Critique Round.
    /// Each agent receives the MESSAGEs of the other two agents (never its own).
    /// </summary>
    public async Task<IReadOnlyList<AgentResponse>> RunCritiqueRoundAsync(
        SessionState state,
        CancellationToken cancellationToken)
    {
        var allAgentIds = _agents.Select(a => a.AgentId).ToList();

        var contexts = _agents.Select(a =>
        {
            var targets = GetCritiqueTargetMessages(a.AgentId, allAgentIds, state.LastRoundMessages);
            return AgentContext.ForCritique(
                state.SessionId,
                state.TruthMap,
                state.FrictionLevel,
                state.CurrentRound,
                targets,
                state.ResearchToolsEnabled);
        }).ToList();

        var responses = await DispatchParallelAsync(_agents, contexts, cancellationToken);
        await ApplyPatchesAsync(state, responses, cancellationToken);
        AccumulateTokens(state, responses);
        return responses;
    }

    /// <summary>
    /// Dispatches the Synthesizer agent for the Synthesis phase.
    /// </summary>
    public async Task<AgentResponse> RunSynthesisAsync(
        SessionState state,
        CancellationToken cancellationToken)
    {
        var synthesizer = _agents.FirstOrDefault(a =>
            a.AgentId == Domain.Agents.AgentId.Synthesizer);

        if (synthesizer is null)
            throw new InvalidOperationException("Synthesizer agent is not registered.");

        var context = AgentContext.ForAnalysis(
            state.SessionId,
            state.TruthMap,
            state.FrictionLevel,
            state.CurrentRound,
            state.ResearchToolsEnabled);

        var response = await RunWithTimeoutAsync(synthesizer, context, cancellationToken);

        if (response.HasPatch)
            await ApplyPatchesAsync(state, [response], cancellationToken);

        AccumulateTokens(state, [response]);
        return response;
    }

    /// <summary>
    /// Dispatches only the specified agent IDs for a Targeted Loop.
    /// </summary>
    public async Task<IReadOnlyList<AgentResponse>> RunTargetedLoopAsync(
        SessionState state,
        IReadOnlyList<string> targetAgentIds,
        string microDirective,
        CancellationToken cancellationToken)
    {
        var targeted = _agents
            .Where(a => targetAgentIds.Contains(a.AgentId))
            .ToList();

        var contexts = targeted.Select(_ =>
            AgentContext.ForTargetedLoop(
                state.SessionId,
                state.TruthMap,
                state.FrictionLevel,
                state.CurrentRound,
                microDirective,
                state.ResearchToolsEnabled))
            .ToList();

        var responses = await DispatchParallelAsync(targeted, contexts, cancellationToken);
        await ApplyPatchesAsync(state, responses, cancellationToken);
        AccumulateTokens(state, responses);
        return responses;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Dispatches all agents concurrently. Each call has its own per-agent timeout.
    /// </summary>
    private async Task<IReadOnlyList<AgentResponse>> DispatchParallelAsync(
        IReadOnlyList<ICouncilAgent> agents,
        IReadOnlyList<AgentContext> contexts,
        CancellationToken sessionCancellationToken)
    {
        var tasks = agents.Select((agent, i) =>
            RunWithTimeoutAsync(agent, contexts[i], sessionCancellationToken)).ToList();

        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Wraps a single agent call with a per-agent timeout linked to the session token.
    /// Timed-out agents return a <see cref="AgentResponse.TimedOut"/> stub.
    /// </summary>
    private async Task<AgentResponse> RunWithTimeoutAsync(
        ICouncilAgent agent,
        AgentContext context,
        CancellationToken sessionCancellationToken)
    {
        using var agentTimeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(_agentTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            agentTimeout.Token, sessionCancellationToken);

        try
        {
            return await agent.RunAsync(context, linked.Token);
        }
        catch (OperationCanceledException ex) when (agentTimeout.IsCancellationRequested)
        {
            _logger?.LogWarning(
                ex,
                "Agent {AgentId} timed out after {TimeoutSeconds}s",
                agent.AgentId, _agentTimeoutSeconds);
            return AgentResponse.CreateTimedOut(agent.AgentId);
        }
    }

    /// <summary>
    /// Applies valid patches from the responses to the Truth Map in alphabetical agent-ID order.
    /// Invalid patches are logged and skipped.
    /// </summary>
    private async Task ApplyPatchesAsync(
        SessionState state,
        IReadOnlyList<AgentResponse> responses,
        CancellationToken cancellationToken)
    {
        var orderedWithPatches = responses
            .Where(r => r.HasPatch && !r.TimedOut)
            .OrderBy(r => r.AgentId, StringComparer.Ordinal);

        foreach (var response in orderedWithPatches)
        {
            var patch = response.Patch!;
            var validationResult = Domain.TruthMap.PatchValidator.Validate(patch, state.TruthMap);

            if (!validationResult.IsValid)
            {
                _logger?.LogWarning(
                    "Patch from agent {AgentId} rejected: {Reason}",
                    response.AgentId, validationResult.Reason);
                continue;
            }

            state.TruthMap = await _truthMapRepository.ApplyPatchAsync(
                state.SessionId, patch, cancellationToken);

            await _broadcaster.SendTruthMapPatchAsync(
                state.SessionId, patch, state.TruthMap.Version, cancellationToken);
        }
    }

    private static void AccumulateTokens(SessionState state, IReadOnlyList<AgentResponse> responses)
    {
        foreach (var r in responses)
            state.TokensUsed += r.TokensUsed;
    }

    /// <summary>
    /// Returns the prior-round messages for the agents that the given agent should critique.
    /// The calling agent's own message is excluded.
    /// </summary>
    private static IReadOnlyList<AgentMessage> GetCritiqueTargetMessages(
        string agentId,
        IReadOnlyList<string> allAgentIds,
        Dictionary<string, string> lastRoundMessages)
    {
        return allAgentIds
            .Where(id => id != agentId && lastRoundMessages.ContainsKey(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new AgentMessage(id, lastRoundMessages[id]))
            .ToList();
    }
}
