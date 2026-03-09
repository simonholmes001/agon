using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Services;
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
    private readonly ConversationHistoryService _conversationHistory;
    private readonly int _agentTimeoutSeconds;
    private readonly ILogger<AgentRunner>? _logger;

    public AgentRunner(
        IReadOnlyList<ICouncilAgent> agents,
        ITruthMapRepository truthMapRepository,
        IEventBroadcaster broadcaster,
        ConversationHistoryService conversationHistory,
        int agentTimeoutSeconds = 90,
        ILogger<AgentRunner>? logger = null)
    {
        _agents = agents;
        _truthMapRepository = truthMapRepository;
        _broadcaster = broadcaster;
        _conversationHistory = conversationHistory;
        _agentTimeoutSeconds = agentTimeoutSeconds;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the Moderator agent for the Clarification phase.
    /// </summary>
    public async Task<AgentResponse> RunModeratorAsync(
        SessionState state,
        CancellationToken cancellationToken)
    {
        var moderator = _agents.FirstOrDefault(a => a.AgentId == Domain.Agents.AgentId.Moderator);
        
        if (moderator is null)
        {
            throw new InvalidOperationException("Moderator agent not configured");
        }

        var context = AgentContext.ForClarification(
            state.SessionId,
            state.TruthMap,
            state.FrictionLevel,
            state.ClarificationRoundCount,
            state.UserMessages,
            false); // Research tools not used during clarification

        var response = await RunWithTimeoutAsync(moderator, context, cancellationToken);

        // Apply patches (if any) from the Moderator's response
        if (response.HasPatch)
        {
            await ApplyPatchesAsync(state, [response], cancellationToken);
        }

        AccumulateTokens(state, [response]);

        _logger?.LogInformation(
            "Moderator completed for session {SessionId}, round {Round}. Tokens: {Tokens}",
            state.SessionId,
            state.ClarificationRoundCount,
            response.TokensUsed);

        // Log and persist the Moderator's message
        if (!string.IsNullOrWhiteSpace(response.Message))
        {
            _logger?.LogInformation(
                "Moderator message: {Message}",
                response.Message.Length > 500 ? response.Message.Substring(0, 500) + "..." : response.Message);
            
            // Store message in conversation history for CLI retrieval
            await _conversationHistory.StoreMessageAsync(
                state.SessionId,
                "moderator",
                response.Message,
                state.ClarificationRoundCount,
                cancellationToken);
        }

        return response;
    }

    /// <summary>
    /// Runs the dedicated post-delivery assistant for follow-up Q&A and revisions.
    /// </summary>
    public async Task<AgentResponse> RunPostDeliveryFollowUpAsync(
        SessionState state,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var assistant = _agents.FirstOrDefault(a => a.AgentId == Domain.Agents.AgentId.PostDeliveryAssistant);
        if (assistant is null)
        {
            throw new InvalidOperationException("Post-delivery assistant agent not configured");
        }

        var history = await _conversationHistory.GetMessagesAsync(state.SessionId, cancellationToken);
        var priorContext = BuildPostDeliveryContext(history);

        var context = AgentContext.ForPostDelivery(
            state.SessionId,
            state.TruthMap,
            state.FrictionLevel,
            state.CurrentRound,
            state.UserMessages,
            priorContext,
            state.ResearchToolsEnabled);

        var response = await RunWithTimeoutAsync(assistant, context, cancellationToken);
        AccumulateTokens(state, [response]);

        if (!response.TimedOut && !string.IsNullOrWhiteSpace(response.Message))
        {
            await _conversationHistory.StoreMessageAsync(
                state.SessionId,
                assistant.AgentId,
                response.Message,
                state.CurrentRound,
                cancellationToken);
        }

        _logger?.LogInformation(
            "Post-delivery assistant completed for session {SessionId}. User message length: {Length}, Tokens: {Tokens}",
            state.SessionId,
            userMessage.Length,
            response.TokensUsed);

        return response;
    }

    /// <summary>
    /// Dispatches all council agents in parallel for the Analysis Round.
    /// Applies valid patches sequentially (alphabetical order) then updates token budget.
    /// Stores all agent messages to conversation history for CLI retrieval.
    /// </summary>
    public async Task<IReadOnlyList<AgentResponse>> RunAnalysisRoundAsync(
        SessionState state,
        CancellationToken cancellationToken)
    {
        var councilAgents = _agents
            .Where(a => Domain.Agents.AgentId.CouncilAgents.Contains(a.AgentId))
            .OrderBy(a => a.AgentId)
            .ToList();

        var contexts = councilAgents.Select(_ =>
            AgentContext.ForAnalysis(
                state.SessionId,
                state.TruthMap,
                state.FrictionLevel,
                state.CurrentRound,
                state.ResearchToolsEnabled))
            .ToList();

        var responses = await DispatchParallelAsync(councilAgents, contexts, cancellationToken);
        await ApplyPatchesAsync(state, responses, cancellationToken);
        AccumulateTokens(state, responses);

        // Store all agent messages so the CLI can fetch them
        foreach (var response in responses.Where(r => !r.TimedOut && !string.IsNullOrWhiteSpace(r.Message)))
        {
            await _conversationHistory.StoreMessageAsync(
                state.SessionId,
                response.AgentId,
                response.Message,
                state.CurrentRound,
                cancellationToken);
        }

        return responses;
    }

    /// <summary>
    /// Dispatches all council agents in parallel for the Critique Round.
    /// Each agent receives the MESSAGEs of the other two agents (never its own).
    /// Stores all agent messages to conversation history for CLI retrieval.
    /// </summary>
    public async Task<IReadOnlyList<AgentResponse>> RunCritiqueRoundAsync(
        SessionState state,
        CancellationToken cancellationToken)
    {
        var councilAgents = _agents
            .Where(a => Domain.Agents.AgentId.CouncilAgents.Contains(a.AgentId))
            .OrderBy(a => a.AgentId)
            .ToList();

        var allCouncilIds = councilAgents.Select(a => a.AgentId).ToList();

        var contexts = councilAgents.Select(a =>
        {
            var targets = GetCritiqueTargetMessages(a.AgentId, allCouncilIds, state.LastRoundMessages);
            return AgentContext.ForCritique(
                state.SessionId,
                state.TruthMap,
                state.FrictionLevel,
                state.CurrentRound,
                targets,
                state.ResearchToolsEnabled);
        }).ToList();

        var responses = await DispatchParallelAsync(councilAgents, contexts, cancellationToken);
        await ApplyPatchesAsync(state, responses, cancellationToken);
        AccumulateTokens(state, responses);

        // Store all critique agent messages so the CLI can fetch them
        foreach (var response in responses.Where(r => !r.TimedOut && !string.IsNullOrWhiteSpace(r.Message)))
        {
            await _conversationHistory.StoreMessageAsync(
                state.SessionId,
                response.AgentId + "_critique",
                response.Message,
                state.CurrentRound,
                cancellationToken);
        }

        return responses;
    }

    /// <summary>
    /// Dispatches the Synthesizer agent for the Synthesis phase.
    /// Stores the synthesizer message to conversation history for CLI retrieval.
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

        // Store synthesizer message so the CLI can fetch it
        if (!response.TimedOut && !string.IsNullOrWhiteSpace(response.Message))
        {
            await _conversationHistory.StoreMessageAsync(
                state.SessionId,
                response.AgentId,
                response.Message,
                state.CurrentRound,
                cancellationToken);
        }

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
        using var agentTimeout = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            agentTimeout.Token, sessionCancellationToken);

        try
        {
            var runTask = agent.RunAsync(context, linked.Token);
            var timeoutTask = Task.Delay(
                TimeSpan.FromSeconds(_agentTimeoutSeconds),
                CancellationToken.None);

            var completedTask = await Task.WhenAny(runTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                // Best-effort cancellation for providers that honor cancellation tokens.
                agentTimeout.Cancel();

                _logger?.LogWarning(
                    "Agent {AgentId} timed out after {TimeoutSeconds}s (hard timeout)",
                    agent.AgentId, _agentTimeoutSeconds);
                return AgentResponse.CreateTimedOut(agent.AgentId);
            }

            return await runTask;
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

    private static string? BuildPostDeliveryContext(IReadOnlyList<AgentMessageRecord> history)
    {
        var relevant = history
            .Where(m => m.AgentId is Domain.Agents.AgentId.Synthesizer or Domain.Agents.AgentId.PostDeliveryAssistant)
            .TakeLast(6)
            .ToList();

        if (relevant.Count == 0)
        {
            return null;
        }

        var lines = new List<string>
        {
            "Use this prior assistant context when preparing your answer:"
        };

        foreach (var message in relevant)
        {
            lines.Add($"[{message.AgentId} | round {message.Round}]");
            lines.Add(message.Message);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
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
