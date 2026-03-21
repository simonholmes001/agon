using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Services;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    private const string ModeratorPatchRepairDirective = """
        Repair your previous response to conform exactly to required structured format.
        Keep your substantive reasoning, but output strict sections:
        1) ## MESSAGE with first line exactly STATUS: DIRECT_ANSWER, STATUS: NEEDS_INFO, or STATUS: READY
        2) ## PATCH with valid JSON matching TruthMapPatch schema.
        Patch meta must use:
        - agent: moderator
        - round: current clarification round
        - session_id: current session id
        If you cannot make a valid patch, output an empty ops array with valid meta.
        """;
    private const string ModeratorDirectAnswerDirective = """
        The latest user input is a simple/meta question about Agon itself.
        Do NOT ask clarification questions.
        Provide a direct, concise, user-facing explanation.
        First line in MESSAGE must be exactly: STATUS: DIRECT_ANSWER
        PATCH must be valid JSON with empty ops and correct moderator meta.
        """;
    private const string ModeratorIntentRoutingDirective = """
        Route the latest user input before answering.
        Return first line exactly as one of:
        - ROUTE: DIRECT_ANSWER
        - ROUTE: FULL_DEBATE
        Use DIRECT_ANSWER for general/meta questions about Agon itself (capabilities, commands, architecture, models/agents, how it works).
        Use FULL_DEBATE for product/spec/debate work that should run the full multi-agent chain.
        Then provide one short reason line.
        Do not ask clarifying questions.
        """;
    private static readonly Regex ModeratorStatusRegex = new(
        @"^\s*(?:status|clarification_status)\s*[:=]\s*(DIRECT_ANSWER|READY|NEEDS_INFO)\b",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ModeratorRouteRegex = new(
        @"^\s*route\s*[:=]\s*(DIRECT_ANSWER|FULL_DEBATE)\b",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnalysisIntentRegex = new(
        @"\b(prd|product requirements?|debate brief|architecture|roadmap|mvp|spec(?:ification)?|analysis|analy[sz]e|evaluate|compare|implementation|implement|build|design|tech stack|user stor(?:y|ies))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SimpleMetaQueryRegex = new(
        @"\b(what can you do|how can you help|who are you|what is agon|internal setup|your setup|how do you work|capabilities|command(?:s)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SelfReferenceRegex = new(
        @"\b(agon|you|your|this assistant|this tool|this cli)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SystemMetaTopicRegex = new(
        @"\b(agent(?:s)?|llm(?:s)?|model(?:s)?|internal|setup|architecture|capabilit(?:y|ies)|command(?:s)?|work(?:s|ing)?|help)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex QuestionLeadRegex = new(
        @"^\s*(how|what|who|can|could|would|do|does|is|are|where|when)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            false, // Research tools not used during clarification
            state.Attachments);

        var shouldRunIntentRouter = ShouldRunIntentRouter(state);
        var routeDecision = await DetermineModeratorRouteAsync(
            moderator,
            context,
            state,
            shouldRunIntentRouter,
            cancellationToken);
        var shouldForceDirectAnswer = routeDecision.ShouldForceDirectAnswer;

        if (shouldForceDirectAnswer)
        {
            context = context with { MicroDirective = ModeratorDirectAnswerDirective };
        }

        var response = await RunWithTimeoutAsync(moderator, context, cancellationToken);
        response = await RetryModeratorOnceForDirectAnswerAsync(
            moderator,
            context,
            shouldForceDirectAnswer,
            response,
            cancellationToken);
        response = await RetryModeratorOnceIfMalformedAsync(
            moderator,
            context,
            state,
            response,
            cancellationToken);

        if (response.HasPatch && GetModeratorPatchValidationError(response.Patch!, state) is not null)
        {
            // Strict guard: never attempt to apply a moderator patch that fails validation.
            _logger?.LogWarning(
                "Moderator patch still invalid after repair attempt. Patch dropped for session {SessionId}.",
                state.SessionId);
            response = response with { Patch = null };
        }

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
        
        // Persist moderator message for CLI retrieval.
        if (!string.IsNullOrWhiteSpace(response.Message))
        {
            await _conversationHistory.StoreMessageAsync(
                state.SessionId,
                "moderator",
                response.Message,
                state.ClarificationRoundCount,
                cancellationToken);
        }

        return response;
    }

    private async Task<AgentResponse> RetryModeratorOnceForDirectAnswerAsync(
        ICouncilAgent moderator,
        AgentContext context,
        bool shouldForceDirectAnswer,
        AgentResponse response,
        CancellationToken cancellationToken)
    {
        if (!shouldForceDirectAnswer || response.TimedOut)
        {
            return response;
        }

        var status = ParseModeratorStatus(response.Message);
        if (status is "DIRECT_ANSWER")
        {
            return response;
        }

        _logger?.LogInformation(
            "Moderator returned {Status} while direct-answer routing was selected. Retrying once with enforced direct-answer directive.",
            status ?? "UNKNOWN");

        var retryContext = context with { MicroDirective = ModeratorDirectAnswerDirective };
        return await RunWithTimeoutAsync(moderator, retryContext, cancellationToken);
    }

    private async Task<ModeratorRouteDecision> DetermineModeratorRouteAsync(
        ICouncilAgent moderator,
        AgentContext context,
        SessionState state,
        bool shouldRunIntentRouter,
        CancellationToken cancellationToken)
    {
        if (!shouldRunIntentRouter)
        {
            return new ModeratorRouteDecision(false, "router_not_run");
        }

        try
        {
            var routeContext = context with { MicroDirective = ModeratorIntentRoutingDirective };
            var routeResponse = await RunWithTimeoutAsync(moderator, routeContext, cancellationToken);
            var route = ParseModeratorRoute(routeResponse.Message);

            if (route == ModeratorRoute.DirectAnswer)
            {
                _logger?.LogInformation(
                    "Moderator intent router selected DIRECT_ANSWER for session {SessionId}.",
                    state.SessionId);
                return new ModeratorRouteDecision(true, "llm_router");
            }

            if (route == ModeratorRoute.FullDebate)
            {
                _logger?.LogInformation(
                    "Moderator intent router selected FULL_DEBATE for session {SessionId}.",
                    state.SessionId);
                return new ModeratorRouteDecision(false, "llm_router");
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            _logger?.LogWarning(
                ex,
                "Moderator intent router failed for session {SessionId}; falling back to deterministic heuristics.",
                state.SessionId);
        }

        var fallback = ShouldHandleAsSimpleMetaQuery(state);
        _logger?.LogInformation(
            "Moderator intent router fell back to deterministic heuristics (directAnswer={DirectAnswer}) for session {SessionId}.",
            fallback,
            state.SessionId);
        return new ModeratorRouteDecision(fallback, "fallback");
    }

    private async Task<AgentResponse> RetryModeratorOnceIfMalformedAsync(
        ICouncilAgent moderator,
        AgentContext context,
        SessionState state,
        AgentResponse response,
        CancellationToken cancellationToken)
    {
        if (response.TimedOut)
        {
            return response;
        }

        var initialError = GetModeratorResponseValidationError(response, state);
        if (initialError is null)
        {
            return response;
        }

        _logger?.LogWarning(
            "Moderator response validation failed for session {SessionId}: {ValidationError}. Retrying once with repair directive.",
            state.SessionId,
            initialError);

        var repairContext = context with
        {
            MicroDirective = ModeratorPatchRepairDirective
        };

        var repaired = await RunWithTimeoutAsync(moderator, repairContext, cancellationToken);
        if (repaired.TimedOut)
        {
            _logger?.LogWarning(
                "Moderator repair retry timed out for session {SessionId}. Using original response.",
                state.SessionId);
            return response;
        }

        var repairedError = GetModeratorResponseValidationError(repaired, state);
        if (repairedError is not null)
        {
            _logger?.LogWarning(
                "Moderator repair retry still invalid for session {SessionId}: {ValidationError}.",
                state.SessionId,
                repairedError);

            // Keep repaired MESSAGE if any, but drop malformed patch.
            return repaired with { Patch = null };
        }

        return repaired;
    }

    private static string? GetModeratorResponseValidationError(AgentResponse response, SessionState state)
    {
        var hasPatchHeader = ContainsPatchHeader(response.RawOutput);

        if (hasPatchHeader && response.Patch is null)
        {
            return "PATCH section present but could not be parsed.";
        }

        if (response.Patch is null)
        {
            return null;
        }

        return GetModeratorPatchValidationError(response.Patch, state);
    }

    private static string? GetModeratorPatchValidationError(TruthMapPatch patch, SessionState state)
    {
        if (!string.Equals(patch.Meta.Agent, Domain.Agents.AgentId.Moderator, StringComparison.Ordinal))
        {
            return $"Patch meta.agent must be '{Domain.Agents.AgentId.Moderator}'.";
        }

        if (patch.Meta.SessionId != state.SessionId)
        {
            return "Patch meta.session_id does not match session.";
        }

        if (patch.Meta.Round != state.ClarificationRoundCount)
        {
            return "Patch meta.round does not match clarification round.";
        }

        var validation = Domain.TruthMap.PatchValidator.Validate(patch, state.TruthMap);
        return validation.IsValid ? null : validation.Reason ?? "Patch failed validation.";
    }

    private static bool ContainsPatchHeader(string? rawOutput) =>
        !string.IsNullOrWhiteSpace(rawOutput)
        && rawOutput.Contains("## PATCH", StringComparison.OrdinalIgnoreCase);

    private static string? ParseModeratorStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var match = ModeratorStatusRegex.Match(message);
        return match.Success ? match.Groups[1].Value.Trim().ToUpperInvariant() : null;
    }

    private static ModeratorRoute ParseModeratorRoute(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return ModeratorRoute.Unknown;
        }

        var routeMatch = ModeratorRouteRegex.Match(message);
        if (routeMatch.Success)
        {
            return routeMatch.Groups[1].Value.Trim().ToUpperInvariant() switch
            {
                "DIRECT_ANSWER" => ModeratorRoute.DirectAnswer,
                "FULL_DEBATE" => ModeratorRoute.FullDebate,
                _ => ModeratorRoute.Unknown
            };
        }

        return ModeratorRoute.Unknown;
    }

    private static bool ShouldRunIntentRouter(SessionState state)
    {
        var latestInput = GetLatestUserInput(state);
        if (string.IsNullOrWhiteSpace(latestInput))
        {
            return false;
        }

        var trimmed = latestInput.Trim();
        if (trimmed.Length is < 4 or > 600)
        {
            return false;
        }

        return trimmed.Contains('?') || QuestionLeadRegex.IsMatch(trimmed);
    }

    private static bool ShouldHandleAsSimpleMetaQuery(SessionState state)
    {
        var latestInput = GetLatestUserInput(state);
        if (string.IsNullOrWhiteSpace(latestInput))
        {
            return false;
        }

        return LooksLikeSimpleMetaQuery(latestInput);
    }

    private static string GetLatestUserInput(SessionState state)
    {
        if (state.UserMessages.Count > 0)
        {
            return state.UserMessages[^1].Content;
        }

        return state.Idea ?? string.Empty;
    }

    private static bool LooksLikeSimpleMetaQuery(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length is < 4 or > 280)
        {
            return false;
        }

        var looksLikeQuestion = trimmed.Contains('?') || QuestionLeadRegex.IsMatch(trimmed);
        if (!looksLikeQuestion)
        {
            return false;
        }

        if (SimpleMetaQueryRegex.IsMatch(trimmed))
        {
            return true;
        }

        var selfReferential = SelfReferenceRegex.IsMatch(trimmed);
        var systemTopic = SystemMetaTopicRegex.IsMatch(trimmed);
        var strongProjectSignal = AnalysisIntentRegex.IsMatch(trimmed)
            && !selfReferential;

        if (strongProjectSignal)
        {
            return false;
        }

        return selfReferential && systemTopic;
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
            state.ResearchToolsEnabled,
            state.Attachments);

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
                state.ResearchToolsEnabled,
                state.Attachments))
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
                state.ResearchToolsEnabled,
                state.Attachments);
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

        var context = AgentContext.ForSynthesis(
            state.SessionId,
            state.TruthMap,
            state.FrictionLevel,
            state.CurrentRound,
            state.ResearchToolsEnabled,
            state.Attachments);

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
                state.ResearchToolsEnabled,
                state.Attachments))
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

            try
            {
                state.TruthMap = await _truthMapRepository.ApplyPatchAsync(
                    state.SessionId, patch, cancellationToken);

                await _broadcaster.SendTruthMapPatchAsync(
                    state.SessionId, patch, state.TruthMap.Version, cancellationToken);
            }
            catch (Exception ex) when (
                ex is JsonException
                || ex is InvalidOperationException
                || ex is NotSupportedException)
            {
                // Do not fail the whole request on malformed agent patch payloads.
                _logger?.LogWarning(
                    ex,
                    "Patch from agent {AgentId} failed at apply-time and was skipped.",
                    response.AgentId);
            }
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

    private enum ModeratorRoute
    {
        Unknown,
        DirectAnswer,
        FullDebate
    }

    private sealed record ModeratorRouteDecision(bool ShouldForceDirectAnswer, string DecisionSource);
}
