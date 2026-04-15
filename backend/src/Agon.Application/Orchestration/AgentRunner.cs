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
        The latest user input should be handled as a direct answer path.
        Do NOT ask clarification questions.
        Answer only the user's latest request with a direct, concise explanation.
        Do NOT include Agon workflow/process context unless explicitly asked.
        For straightforward factual or utility requests, keep output plain and minimal.
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
    private const string PostDeliveryCouncilProposalDirective = """
        This follow-up request is likely better served by full council analysis.
        In your reply, explicitly offer council invocation.
        Include this exact sentence once:
        "I can invoke the full agent council for a deeper cross-agent answer. Reply with 'invoke council' if you want me to run it."
        Then provide a short provisional answer based on current context.
        Keep the response concise and practical.
        """;
    private static readonly Regex ModeratorStatusRegex = new(
        @"^\s*(?:status|clarification_status)\s*[:=]\s*(DIRECT_ANSWER|READY|NEEDS_INFO)\b",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ModeratorRouteRegex = new(
        @"^\s*route\s*[:=]\s*(DIRECT_ANSWER|FULL_DEBATE)\b",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] CouncilContributionAgentIds =
    [
        Domain.Agents.AgentId.GptAgent,
        Domain.Agents.AgentId.GeminiAgent,
        Domain.Agents.AgentId.ClaudeAgent
    ];
    private const string CritiqueAgentSuffix = "_critique";

    private readonly IReadOnlyList<ICouncilAgent> _agents;
    private readonly Dictionary<string, string> _modelProviderByAgentId;
    private readonly ITruthMapRepository _truthMapRepository;
    private readonly ITokenUsageRepository? _tokenUsageRepository;
    private readonly IEventBroadcaster _broadcaster;
    private readonly ConversationHistoryService _conversationHistory;
    private readonly int _agentTimeoutSeconds;
    private readonly ILogger<AgentRunner>? _logger;

    public AgentRunner(
        IReadOnlyList<ICouncilAgent> agents,
        ITruthMapRepository truthMapRepository,
        IEventBroadcaster broadcaster,
        ConversationHistoryService conversationHistory,
        ITokenUsageRepository? tokenUsageRepository = null,
        int agentTimeoutSeconds = 90,
        ILogger<AgentRunner>? logger = null)
    {
        _agents = agents;
        _modelProviderByAgentId = _agents
            .GroupBy(agent => agent.AgentId)
            .ToDictionary(group => group.Key, group => group.First().ModelProvider, StringComparer.Ordinal);
        _truthMapRepository = truthMapRepository;
        _tokenUsageRepository = tokenUsageRepository;
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

        var shouldRunIntentRouter = ModeratorRoutingClassifier.ShouldRunIntentRouter(state);
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
        response = CoerceModeratorDirectAnswerIfRequired(
            response,
            shouldForceDirectAnswer,
            state);

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

        await AccumulateTokensAsync(state, [response], cancellationToken);

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
        if (ModeratorRoutingClassifier.ShouldForceCouncilPath(state))
        {
            _logger?.LogInformation(
                "Deterministic moderator classifier selected FULL_DEBATE for session {SessionId}.",
                state.SessionId);
            return new ModeratorRouteDecision(false, "deterministic_council");
        }

        if (ModeratorRoutingClassifier.ShouldForceDirectAnswer(state))
        {
            _logger?.LogInformation(
                "Deterministic moderator classifier selected DIRECT_ANSWER for session {SessionId}.",
                state.SessionId);
            return new ModeratorRouteDecision(true, "deterministic");
        }

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
                if (ModeratorRoutingClassifier.ShouldForceCouncilPath(state))
                {
                    _logger?.LogInformation(
                        "Moderator intent router suggested DIRECT_ANSWER, but council-path override applied for session {SessionId}.",
                        state.SessionId);
                    return new ModeratorRouteDecision(false, "llm_router_overridden_to_council");
                }

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

        var fallback = ModeratorRoutingClassifier.ShouldForceDirectAnswer(state);
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

    private static AgentResponse CoerceModeratorDirectAnswerIfRequired(
        AgentResponse response,
        bool shouldForceDirectAnswer,
        SessionState state)
    {
        if (!shouldForceDirectAnswer || response.TimedOut)
        {
            return response;
        }

        var status = ParseModeratorStatus(response.Message);
        var body = StripLeadingStatusLine(response.Message);
        if (string.IsNullOrWhiteSpace(body))
        {
            body = "I can help with that.";
        }

        var message = status == "DIRECT_ANSWER"
            ? response.Message
            : $"STATUS: DIRECT_ANSWER\n{body}";

        return response with
        {
            Message = message,
            Patch = BuildEmptyModeratorPatch(state, "Direct-answer path: no Truth Map mutation.")
        };
    }

    private static string StripLeadingStatusLine(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var normalized = message.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length == 0)
        {
            return normalized.Trim();
        }

        if (!ModeratorStatusRegex.IsMatch(lines[0]))
        {
            return normalized.Trim();
        }

        return string.Join('\n', lines.Skip(1)).Trim();
    }

    private static TruthMapPatch BuildEmptyModeratorPatch(SessionState state, string reason) =>
        new(
            [],
            new PatchMeta(
                Domain.Agents.AgentId.Moderator,
                state.ClarificationRoundCount,
                reason,
                state.SessionId));

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

    /// <summary>
    /// Runs the dedicated post-delivery assistant for follow-up Q&A and revisions.
    /// </summary>
    public async Task<AgentResponse> RunPostDeliveryFollowUpAsync(
        SessionState state,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var councilDecision = PostDeliveryCouncilClassifier.Classify(state, userMessage);
        if (councilDecision == PostDeliveryCouncilDecision.Invoke)
        {
            return await RunPostDeliveryCouncilAsync(state, userMessage, cancellationToken);
        }

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
            councilDecision == PostDeliveryCouncilDecision.Propose
                ? $"{priorContext}\n\n{PostDeliveryCouncilProposalDirective}"
                : priorContext,
            state.ResearchToolsEnabled,
            state.Attachments);

        var response = await RunWithTimeoutAsync(assistant, context, cancellationToken);
        await AccumulateTokensAsync(state, [response], cancellationToken);

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

    private async Task<AgentResponse> RunPostDeliveryCouncilAsync(
        SessionState state,
        string userMessage,
        CancellationToken cancellationToken)
    {
        if (state.Attachments.Count > 0 && !state.Attachments.Any(a => !string.IsNullOrWhiteSpace(a.ExtractedText)))
        {
            _logger?.LogWarning(
                "Post-delivery council invocation for session {SessionId} has no extracted attachment text yet. ReasonCode={ReasonCode}",
                state.SessionId,
                "ATTACHMENT_CONTEXT_PENDING");
        }

        _logger?.LogInformation(
            "Post-delivery council invocation accepted for session {SessionId}. ReasonCode={ReasonCode}",
            state.SessionId,
            "COUNCIL_INVOKED_BY_USER");

        var analysisResponses = await RunAnalysisRoundAsync(state, cancellationToken);
        state.LastRoundMessages.Clear();
        foreach (var response in analysisResponses.Where(r => !r.TimedOut && !string.IsNullOrWhiteSpace(r.Message)))
        {
            state.LastRoundMessages[response.AgentId] = response.Message;
        }

        await RunCritiqueRoundAsync(state, cancellationToken);
        var synthesisResponse = await RunSynthesisAsync(state, cancellationToken);

        _logger?.LogInformation(
            "Post-delivery council invocation completed for session {SessionId}. User message length: {Length}, Tokens: {Tokens}",
            state.SessionId,
            userMessage.Length,
            synthesisResponse.TokensUsed);

        return synthesisResponse;
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
        await AccumulateTokensAsync(state, responses, cancellationToken);

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
        await AccumulateTokensAsync(state, responses, cancellationToken);

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

        await AccumulateTokensAsync(state, [response], cancellationToken);

        if (!response.TimedOut && !string.IsNullOrWhiteSpace(response.Message))
        {
            var contributionBlock = await BuildCouncilContributionBlockAsync(state, cancellationToken);
            if (!string.IsNullOrWhiteSpace(contributionBlock))
            {
                response = response with
                {
                    Message = $"{response.Message.TrimEnd()}\n\n{contributionBlock}"
                };
            }
        }

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

    private async Task<string?> BuildCouncilContributionBlockAsync(
        SessionState state,
        CancellationToken cancellationToken)
    {
        var history = await _conversationHistory.GetMessagesAsync(state.SessionId, cancellationToken);
        var contributionUnitsByAgent = CouncilContributionAgentIds.ToDictionary(
            agentId => agentId,
            _ => 0,
            StringComparer.Ordinal);

        foreach (var message in history.Where(m => m.Round == state.CurrentRound))
        {
            var baseAgentId = ResolveContributionAgentId(message.AgentId);
            if (baseAgentId is null)
            {
                continue;
            }

            contributionUnitsByAgent[baseAgentId] += EstimateContributionUnits(message.Message);
        }

        var totalUnits = contributionUnitsByAgent.Values.Sum();
        var (percentages, reasonCode) = totalUnits <= 0
            ? (BuildLowSignalFallbackPercentages(), "LOW_SIGNAL_FALLBACK")
            : (CalculateRoundedPercentages(contributionUnitsByAgent, totalUnits), "TOKEN_WEIGHTED");
        var ordered = CouncilContributionAgentIds.Select(agentId =>
            $"{agentId}: {percentages[agentId]}%");

        _logger?.LogInformation(
            "Council contribution breakdown computed for session {SessionId}. ReasonCode={ReasonCode}, gpt_agent={GptPercent}, gemini_agent={GeminiPercent}, claude_agent={ClaudePercent}, totalUnits={TotalUnits}",
            state.SessionId,
            reasonCode,
            percentages[Domain.Agents.AgentId.GptAgent],
            percentages[Domain.Agents.AgentId.GeminiAgent],
            percentages[Domain.Agents.AgentId.ClaudeAgent],
            totalUnits);

        return "## Council Contributions\n"
             + "_Percentages estimate each council member's share of contribution signals in this answer._\n"
             + string.Join('\n', ordered.Select(line => $"- {line}"));
    }

    private static Dictionary<string, int> BuildLowSignalFallbackPercentages()
    {
        var fallback = CouncilContributionAgentIds.ToDictionary(
            agentId => agentId,
            _ => 33,
            StringComparer.Ordinal);
        fallback[CouncilContributionAgentIds[0]] = 34;
        return fallback;
    }

    private static string? ResolveContributionAgentId(string agentId)
    {
        foreach (var councilAgentId in CouncilContributionAgentIds)
        {
            if (string.Equals(agentId, councilAgentId, StringComparison.Ordinal))
            {
                return councilAgentId;
            }

            if (string.Equals(agentId, councilAgentId + CritiqueAgentSuffix, StringComparison.Ordinal))
            {
                return councilAgentId;
            }
        }

        return null;
    }

    private static int EstimateContributionUnits(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return 0;
        }

        return message
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }

    private static Dictionary<string, int> CalculateRoundedPercentages(
        IReadOnlyDictionary<string, int> unitsByAgent,
        int totalUnits)
    {
        var floorByAgent = new Dictionary<string, int>(StringComparer.Ordinal);
        var fractionalByAgent = new List<(string AgentId, double FractionalPart)>();
        var assigned = 0;

        foreach (var agentId in CouncilContributionAgentIds)
        {
            var units = unitsByAgent.TryGetValue(agentId, out var value) ? value : 0;
            var exactPercent = units * 100d / totalUnits;
            var floored = (int)Math.Floor(exactPercent);
            floorByAgent[agentId] = floored;
            assigned += floored;
            fractionalByAgent.Add((agentId, exactPercent - floored));
        }

        var remaining = 100 - assigned;
        foreach (var entry in fractionalByAgent
                     .OrderByDescending(x => x.FractionalPart)
                     .ThenBy(x => x.AgentId, StringComparer.Ordinal)
                     .Take(Math.Max(0, remaining)))
        {
            floorByAgent[entry.AgentId] += 1;
        }

        return floorByAgent;
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
        await AccumulateTokensAsync(state, responses, cancellationToken);
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

    private async Task AccumulateTokensAsync(
        SessionState state,
        IReadOnlyList<AgentResponse> responses,
        CancellationToken cancellationToken)
    {
        foreach (var response in responses)
        {
            state.TokensUsed += Math.Max(0, response.TokensUsed);
        }

        if (_tokenUsageRepository is null || responses.Count == 0)
        {
            return;
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var usageRecords = responses
            .Where(response => !response.TimedOut)
            .Select(response => BuildUsageRecord(state, response, occurredAt))
            .ToList();

        if (usageRecords.Count == 0)
        {
            return;
        }

        await _tokenUsageRepository.AddRangeAsync(usageRecords, cancellationToken);
    }

    private TokenUsageRecord BuildUsageRecord(
        SessionState state,
        AgentResponse response,
        DateTimeOffset occurredAt)
    {
        var modelProvider = _modelProviderByAgentId.TryGetValue(response.AgentId, out var value)
            ? value
            : "unknown unknown";
        var (provider, model) = ParseProviderAndModel(modelProvider);
        var promptTokens = Math.Max(0, response.PromptTokens);
        var completionTokens = Math.Max(0, response.CompletionTokens);
        var totalTokens = Math.Max(0, response.TokensUsed);

        if (promptTokens == 0 && completionTokens == 0 && totalTokens > 0)
        {
            completionTokens = totalTokens;
        }

        if (totalTokens == 0)
        {
            totalTokens = promptTokens + completionTokens;
        }

        var source = string.IsNullOrWhiteSpace(response.TokenUsageSource)
            ? "estimated"
            : response.TokenUsageSource.Trim();

        return new TokenUsageRecord(
            Id: Guid.NewGuid(),
            UserId: state.UserId,
            SessionId: state.SessionId,
            AgentId: response.AgentId,
            Provider: provider,
            Model: model,
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TotalTokens: totalTokens,
            Source: source,
            OccurredAt: occurredAt);
    }

    private static (string Provider, string Model) ParseProviderAndModel(string modelProvider)
    {
        if (string.IsNullOrWhiteSpace(modelProvider))
        {
            return ("unknown", "unknown");
        }

        var firstSpaceIndex = modelProvider.IndexOf(' ');
        if (firstSpaceIndex <= 0 || firstSpaceIndex >= modelProvider.Length - 1)
        {
            return (modelProvider.Trim(), "unknown");
        }

        var provider = modelProvider[..firstSpaceIndex].Trim();
        var model = modelProvider[(firstSpaceIndex + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = "unknown";
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            model = "unknown";
        }

        return (provider, model);
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
