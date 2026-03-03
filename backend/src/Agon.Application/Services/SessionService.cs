using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Sessions;
using Agon.Domain.Agents;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Agon.Application.Services;

public class SessionService(
    ISessionRepository sessionRepository,
    ITruthMapRepository truthMapRepository,
    ITranscriptRepository transcriptRepository,
    Orchestrator orchestrator,
    AgentRunner agentRunner,
    IEnumerable<ICouncilAgent> councilAgents,
    IEventBroadcaster eventBroadcaster,
    ILogger<SessionService> logger)
{
    private const int StreamingChunkDelayMilliseconds = 90;

    public async Task<SessionState> CreateSessionAsync(
        string idea,
        SessionMode mode,
        int frictionLevel,
        CancellationToken cancellationToken)
    {
        var trimmedIdea = idea?.Trim() ?? string.Empty;
        if (trimmedIdea.Length < 10)
        {
            logger.LogWarning(
                "Rejected session creation for short idea. IdeaLength={IdeaLength}",
                trimmedIdea.Length);
            throw new ArgumentException("Idea must be at least 10 non-whitespace characters.", nameof(idea));
        }

        if (frictionLevel is < 0 or > 100)
        {
            logger.LogWarning(
                "Rejected session creation for out-of-range friction. FrictionLevel={FrictionLevel}",
                frictionLevel);
            throw new ArgumentOutOfRangeException(nameof(frictionLevel), "Friction level must be between 0 and 100.");
        }

        var sessionId = Guid.NewGuid();
        var session = new SessionState
        {
            SessionId = sessionId,
            Mode = mode,
            Status = SessionStatus.Active,
            Phase = SessionPhase.Clarification,
            FrictionLevel = frictionLevel,
            RoundPolicy = new RoundPolicy(),
            RoundNumber = 0,
            TargetedLoopCount = 0,
            TokensUsed = 0
        };

        var map = TruthMapState.CreateNew(sessionId);
        map.CoreIdea = trimmedIdea;

        await sessionRepository.CreateAsync(session, cancellationToken);
        await truthMapRepository.CreateAsync(map, cancellationToken);

        logger.LogInformation(
            "Created session. SessionId={SessionId} Mode={Mode} FrictionLevel={FrictionLevel}",
            sessionId,
            mode,
            frictionLevel);

        return session;
    }

    public async Task<SessionState> StartSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        var resolvedCorrelationId = NormalizeCorrelationId(correlationId);
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["SessionId"] = sessionId,
            ["CorrelationId"] = resolvedCorrelationId
        });

        logger.LogInformation("StartSessionAsync begin. SessionId={SessionId} CorrelationId={CorrelationId}",
            sessionId, resolvedCorrelationId);

        var session = await sessionRepository.GetAsync(sessionId, cancellationToken);
        if (session is null)
        {
            logger.LogWarning("Cannot start session — not found. SessionId={SessionId}", sessionId);
            throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
        }

        var map = await truthMapRepository.GetAsync(sessionId, cancellationToken);
        if (map is null)
        {
            logger.LogWarning("Cannot start session — truth map not found. SessionId={SessionId}", sessionId);
            throw new KeyNotFoundException($"Truth map for session '{sessionId}' was not found.");
        }

        logger.LogInformation("Session found. Phase={Phase} Status={Status}", session.Phase, session.Status);

        if (session.Phase != SessionPhase.Clarification)
        {
            logger.LogWarning("StartSession called on session not in Clarification phase. Phase={Phase}", session.Phase);
            await sessionRepository.UpdateAsync(session, cancellationToken);
            return session;
        }

        // ── PHASE 1: CLARIFICATION ─────────────────────────────────────────────
        // Moderator (GPT) runs once (or up to MaxClarificationRounds if user answers questions).
        // For the /start endpoint the user has not yet answered anything, so we run once.
        logger.LogInformation("═══ PHASE: Clarification — running Moderator agent ═══");
        orchestrator.StartClarification(session);
        await BroadcastPhaseStartAsync(session, map, cancellationToken);

        var clarifierAgents = SelectActiveAgentsForPhase(SessionPhase.Clarification);
        logger.LogInformation("Clarification agents selected. Count={Count} Agents={Agents}",
            clarifierAgents.Count,
            string.Join(", ", clarifierAgents.Select(a => a.AgentId)));

        if (clarifierAgents.Count == 0)
        {
            logger.LogWarning("No clarification agent configured — skipping clarification phase");
        }
        else
        {
            await RunPhaseRoundAsync(session, map, SessionPhase.Clarification, clarifierAgents, resolvedCorrelationId, cancellationToken);
        }

        // ── PHASE 2: CONSTRUCTION ──────────────────────────────────────────────
        // GPT, Gemini, Claude all run IN PARALLEL.
        logger.LogInformation("═══ PHASE: Construction — dispatching GPT + Gemini + Claude in parallel ═══");
        orchestrator.TransitionToConstruction(session);
        map.Round = session.RoundNumber;
        await truthMapRepository.UpdateAsync(map, cancellationToken);
        await BroadcastPhaseStartAsync(session, map, cancellationToken);

        var constructionAgents = SelectActiveAgentsForPhase(SessionPhase.Construction);
        logger.LogInformation("Construction agents selected. Count={Count} Agents={Agents}",
            constructionAgents.Count,
            string.Join(", ", constructionAgents.Select(a => a.AgentId)));

        if (constructionAgents.Count == 0)
        {
            logger.LogWarning("No construction agents configured — cannot proceed");
        }
        else
        {
            await RunPhaseRoundAsync(session, map, SessionPhase.Construction, constructionAgents, resolvedCorrelationId, cancellationToken);
        }

        // ── PHASES 3+4: CRITIQUE → REFINEMENT loop (bounded) ──────────────────
        var continueLoop = true;
        while (continueLoop)
        {
            // ── CRITIQUE ────────────────────────────────────────────────────────
            logger.LogInformation(
                "═══ PHASE: Critique (iteration {Iteration}) ═══",
                session.RefinementIterationCount + 1);
            orchestrator.TransitionToCritique(session);
            await BroadcastPhaseStartAsync(session, map, cancellationToken);

            var critiqueAgents = SelectActiveAgentsForPhase(SessionPhase.Critique);
            logger.LogInformation("Critique agents selected. Count={Count} Agents={Agents}",
                critiqueAgents.Count,
                string.Join(", ", critiqueAgents.Select(a => a.AgentId)));

            string? critiqueMessage = null;
            if (critiqueAgents.Count == 0)
            {
                logger.LogWarning("No critique agent configured — skipping critique phase");
            }
            else
            {
                var critiqueResults = await RunPhaseRoundAsync(
                    session, map, SessionPhase.Critique, critiqueAgents, resolvedCorrelationId, cancellationToken);

                // Capture the critique MESSAGE so it can be injected into refinement prompts
                critiqueMessage = critiqueResults
                    .Where(r => !string.IsNullOrWhiteSpace(r.Message))
                    .Select(r => r.Message)
                    .FirstOrDefault();
                session.LastCritiqueMessage = critiqueMessage;
                logger.LogInformation("Critique message captured. Length={Length}",
                    critiqueMessage?.Length ?? 0);
            }

            // Decide whether to run another refinement
            continueLoop = orchestrator.ShouldContinueRefinement(session);
            if (!continueLoop)
            {
                logger.LogInformation(
                    "Refinement iterations exhausted ({Max}), skipping refinement and advancing to Synthesis",
                    session.RoundPolicy.MaxRefinementIterations);
                break;
            }

            // ── REFINEMENT ──────────────────────────────────────────────────────
            logger.LogInformation(
                "═══ PHASE: Refinement (iteration {Iteration}) ═══",
                session.RefinementIterationCount + 1);
            orchestrator.TransitionToRefinement(session);
            await BroadcastPhaseStartAsync(session, map, cancellationToken);

            var refinementAgents = SelectActiveAgentsForPhase(SessionPhase.Refinement);
            logger.LogInformation("Refinement agents selected. Count={Count} Agents={Agents}",
                refinementAgents.Count,
                string.Join(", ", refinementAgents.Select(a => a.AgentId)));

            if (refinementAgents.Count == 0)
            {
                logger.LogWarning("No refinement agents configured — skipping refinement phase");
            }
            else
            {
                // Inject the critique message as a directive for each agent
                var critiquePreamble = string.IsNullOrWhiteSpace(critiqueMessage)
                    ? []
                    : new List<string> { $"CRITIQUE FEEDBACK TO ADDRESS:\n{critiqueMessage}" };

                await RunPhaseRoundAsync(
                    session, map, SessionPhase.Refinement, refinementAgents,
                    resolvedCorrelationId, cancellationToken, critiquePreamble);
            }

            // After one full Critique→Refinement, check if more loops are allowed
            continueLoop = orchestrator.ShouldContinueRefinement(session);
            logger.LogInformation("After refinement iteration {Iteration}: continueLoop={Continue}",
                session.RefinementIterationCount, continueLoop);
        }

        // ── PHASE 5: SYNTHESIS ─────────────────────────────────────────────────
        logger.LogInformation("═══ PHASE: Synthesis ═══");
        orchestrator.TransitionToSynthesis(session);
        await BroadcastPhaseStartAsync(session, map, cancellationToken);

        var synthesisAgents = SelectActiveAgentsForPhase(SessionPhase.Synthesis);
        logger.LogInformation("Synthesis agents selected. Count={Count} Agents={Agents}",
            synthesisAgents.Count,
            string.Join(", ", synthesisAgents.Select(a => a.AgentId)));

        if (synthesisAgents.Count == 0)
        {
            logger.LogWarning("No synthesis agent configured — falling back to moderator summary");
            // Fall back to the existing streamed summary path
            var allResults = new List<AgentExecutionResult>();
            var fallbackSummary = await CreateModeratorSummaryMessageAsync(
                session, map, allResults, resolvedCorrelationId, cancellationToken);
            if (fallbackSummary is not null)
            {
                await StreamAndStoreTranscriptMessageAsync(session.SessionId, session.RoundNumber, fallbackSummary.Message, cancellationToken);
            }
        }
        else
        {
            await RunPhaseRoundAsync(session, map, SessionPhase.Synthesis, synthesisAgents, resolvedCorrelationId, cancellationToken);
        }

        // ── PHASE 6: DELIVER ───────────────────────────────────────────────────
        map = await truthMapRepository.GetAsync(sessionId, cancellationToken) ?? map;
        orchestrator.TransitionFromSynthesis(session, map);

        logger.LogInformation("═══ PHASE: {Phase} — session complete ═══", session.Phase);

        await eventBroadcaster.RoundProgressAsync(sessionId, session.Phase, cancellationToken);
        await sessionRepository.UpdateAsync(session, cancellationToken);

        logger.LogInformation(
            "StartSessionAsync complete. SessionId={SessionId} FinalPhase={Phase} Status={Status} ClarificationRounds={CR} RefinementIterations={RI} CorrelationId={CorrelationId}",
            sessionId, session.Phase, session.Status,
            session.ClarificationRoundCount, session.RefinementIterationCount,
            resolvedCorrelationId);

        return session;
    }

    private async Task BroadcastPhaseStartAsync(SessionState session, TruthMapState map, CancellationToken cancellationToken)
    {
        map.Round = session.RoundNumber;
        await truthMapRepository.UpdateAsync(map, cancellationToken);
        await eventBroadcaster.RoundProgressAsync(session.SessionId, session.Phase, cancellationToken);
        var kickoffMessage = CreateRoundKickoffSystemMessage(session.SessionId, session.RoundNumber, session.Phase);
        await transcriptRepository.AppendAsync(session.SessionId, kickoffMessage, cancellationToken);
        await eventBroadcaster.TranscriptMessageAppendedAsync(session.SessionId, kickoffMessage, cancellationToken);
    }

    /// <summary>
    /// Runs a council round for the given phase, logs everything verbosely, and returns results.
    /// </summary>
    private async Task<IReadOnlyList<AgentExecutionResult>> RunPhaseRoundAsync(
        SessionState session,
        TruthMapState map,
        SessionPhase phase,
        List<ICouncilAgent> agents,
        string correlationId,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? additionalDirectives = null)
    {
        var context = new AgentContext
        {
            SessionId = session.SessionId,
            CorrelationId = correlationId,
            Round = session.RoundNumber,
            Phase = phase,
            FrictionLevel = session.FrictionLevel,
            TruthMap = map.DeepCopy(),
            MicroDirectives = additionalDirectives ?? Array.Empty<string>()
        };

        var timeout = ResolveRoundTimeout(agents);
        logger.LogInformation(
            "Dispatching {Phase} round. SessionId={SessionId} AgentCount={AgentCount} TimeoutSeconds={TimeoutSeconds} CorrelationId={CorrelationId}",
            phase, session.SessionId, agents.Count, timeout.TotalSeconds, correlationId);

        var storedMessages = 0;
        var results = await agentRunner.RunRoundAsync(
            agents,
            context,
            timeout,
            async (result, token) =>
            {
                logger.LogInformation(
                    "Agent callback fired. Phase={Phase} AgentId={AgentId} HasMessage={HasMessage} HasPatch={HasPatch} TimedOut={TimedOut} Error={Error}",
                    phase, result.AgentId,
                    !string.IsNullOrWhiteSpace(result.Message),
                    result.Patch is not null,
                    result.TimedOut,
                    result.Error ?? "none");

                if (!string.IsNullOrWhiteSpace(result.Message))
                {
                    if (await StreamAndStoreAgentMessageAsync(session.SessionId, session.RoundNumber, result, token))
                    {
                        storedMessages++;
                    }
                    return;
                }

                var transcriptMessage = CreateTranscriptMessageForResult(session.SessionId, session.RoundNumber, result);
                if (transcriptMessage is null)
                {
                    logger.LogDebug("No transcript message to store for agent {AgentId}", result.AgentId);
                    return;
                }

                await transcriptRepository.AppendAsync(session.SessionId, transcriptMessage, token);
                await eventBroadcaster.TranscriptMessageAppendedAsync(session.SessionId, transcriptMessage, token);
                storedMessages++;
            },
            cancellationToken);

        // Synthesizer-like phases get a moderator summary too (except the Critique phase itself)
        var shouldSummarize = phase == SessionPhase.Synthesis
            && results.Any(r => CanonicalizeAgentId(r.AgentId) != CanonicalizeAgentId(AgentId.Synthesizer));
        if (shouldSummarize)
        {
            logger.LogInformation("Creating moderator summary after {Phase} round", phase);
            var summaryMessage = await CreateModeratorSummaryMessageAsync(
                session, map, results, correlationId, cancellationToken);
            if (summaryMessage is not null)
            {
                if (summaryMessage.WasStreamed)
                {
                    await StoreFinalTranscriptMessageAsync(session.SessionId, summaryMessage.Message, cancellationToken);
                    storedMessages++;
                }
                else if (await StreamAndStoreTranscriptMessageAsync(session.SessionId, session.RoundNumber, summaryMessage.Message, cancellationToken))
                {
                    storedMessages++;
                }
            }
        }

        await agentRunner.ApplyValidatedPatchesAsync(session.SessionId, results, cancellationToken);

        var completionMessage = CreateRoundCompletionSystemMessage(session.SessionId, session.RoundNumber, results, storedMessages);
        await transcriptRepository.AppendAsync(session.SessionId, completionMessage, cancellationToken);
        await eventBroadcaster.TranscriptMessageAppendedAsync(session.SessionId, completionMessage, cancellationToken);

        logger.LogInformation(
            "{Phase} round complete. AgentCount={AgentCount} StoredMessages={StoredMessages} TimedOut={TimedOut} Failed={Failed} CorrelationId={CorrelationId}",
            phase, agents.Count, storedMessages,
            results.Count(r => r.TimedOut),
            results.Count(r => r.Error is not null && r.Error != "timeout"),
            correlationId);

        return results;
    }

    public async Task<SessionMessageResult> PostUserMessageAsync(
        Guid sessionId,
        string message,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        var trimmedMessage = message?.Trim() ?? string.Empty;
        if (trimmedMessage.Length == 0)
        {
            logger.LogWarning(
                "Rejected empty moderator message. SessionId={SessionId}",
                sessionId);
            throw new ArgumentException("Message must contain non-whitespace characters.", nameof(message));
        }

        var resolvedCorrelationId = NormalizeCorrelationId(correlationId);

        var session = await sessionRepository.GetAsync(sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
        var map = await truthMapRepository.GetAsync(sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Truth map for session '{sessionId}' was not found.");

        var routedAgentIds = ResolveRoutedAgentIds(session.Phase, trimmedMessage);
        var agents = ResolveAgentsForMessage(routedAgentIds);
        var primaryAgentId = agents.First().AgentId;

        logger.LogInformation(
            "Dispatching moderator message. SessionId={SessionId} Phase={Phase} RoutedAgents={RoutedAgents} MessageLength={MessageLength} CorrelationId={CorrelationId}",
            sessionId,
            session.Phase,
            string.Join(",", agents.Select(candidate => candidate.AgentId)),
            trimmedMessage.Length,
            resolvedCorrelationId);

        var userMessage = new TranscriptMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Type = TranscriptMessageType.User,
            AgentId = null,
            Content = trimmedMessage,
            Round = session.RoundNumber,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await transcriptRepository.AppendAsync(sessionId, userMessage, cancellationToken);
        await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, userMessage, cancellationToken);

        var context = new AgentContext
        {
            SessionId = sessionId,
            CorrelationId = resolvedCorrelationId,
            Round = session.RoundNumber,
            Phase = session.Phase,
            FrictionLevel = session.FrictionLevel,
            TruthMap = map.DeepCopy(),
            MicroDirectives = BuildFollowUpDirectives(trimmedMessage, routedAgentIds)
        };

        try
        {
            if (agents.Count > 1)
            {
                var routingMessage = new TranscriptMessage
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    Type = TranscriptMessageType.System,
                    AgentId = null,
                    Content =
                        $"Moderator routed follow-up to: {string.Join(", ", agents.Select(candidate => PresentableAgentId(candidate.AgentId)))}.",
                    Round = session.RoundNumber,
                    IsStreaming = false,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                await transcriptRepository.AppendAsync(sessionId, routingMessage, cancellationToken);
                await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, routingMessage, cancellationToken);
            }

            var timeout = ResolveRoundTimeout(agents);
            var storedMessages = 0;
            var results = await agentRunner.RunRoundAsync(
                agents,
                context,
                timeout,
                async (result, token) =>
                {
                    if (!string.IsNullOrWhiteSpace(result.Message))
                    {
                        if (await StreamAndStoreAgentMessageAsync(sessionId, session.RoundNumber, result, token))
                        {
                            storedMessages++;
                        }
                        return;
                    }

                    var transcriptMessage = CreateTranscriptMessageForResult(sessionId, session.RoundNumber, result);
                    if (transcriptMessage is null)
                    {
                        return;
                    }

                    await transcriptRepository.AppendAsync(sessionId, transcriptMessage, token);
                    await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, transcriptMessage, token);
                    storedMessages++;
                },
                cancellationToken);

            var shouldSummarize = session.Phase == SessionPhase.PostDelivery
                && results.Any(result =>
                    CanonicalizeAgentId(result.AgentId) != CanonicalizeAgentId(AgentId.Synthesizer));
            if (shouldSummarize)
            {
                var summaryMessage = await CreateModeratorSummaryMessageAsync(
                    session,
                    map,
                    results,
                    resolvedCorrelationId,
                    cancellationToken);
                if (summaryMessage is not null)
                {
                    if (summaryMessage.WasStreamed)
                    {
                        await StoreFinalTranscriptMessageAsync(sessionId, summaryMessage.Message, cancellationToken);
                        storedMessages++;
                    }
                    else if (await StreamAndStoreTranscriptMessageAsync(sessionId, session.RoundNumber, summaryMessage.Message, cancellationToken))
                    {
                        storedMessages++;
                    }
                }
            }

            await agentRunner.ApplyValidatedPatchesAsync(sessionId, results, cancellationToken);

            var primaryResult = results.FirstOrDefault(result =>
                CanonicalizeAgentId(result.AgentId) == CanonicalizeAgentId(primaryAgentId))
                ?? results.FirstOrDefault(result => !string.IsNullOrWhiteSpace(result.Message));

            var reply = !string.IsNullOrWhiteSpace(primaryResult?.Message)
                ? primaryResult!.Message!.Trim()
                : "The moderator has dispatched follow-up responses.";
            var patchApplied = results.Any(result => result.Patch is not null);

            return new SessionMessageResult(
                sessionId,
                session.Phase.ToString(),
                CanonicalizeAgentId(primaryAgentId),
                reply,
                patchApplied);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Moderator-routed agents failed. SessionId={SessionId} Phase={Phase} RoutedAgents={RoutedAgents} CorrelationId={CorrelationId}",
                sessionId,
                session.Phase,
                string.Join(",", agents.Select(candidate => candidate.AgentId)),
                resolvedCorrelationId);

            var failureContent =
                $"Moderator follow-up failed in round {session.RoundNumber}. Reason: {TrimError(exception.Message)}";
            var failureMessage = new TranscriptMessage
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                Type = TranscriptMessageType.System,
                AgentId = null,
                Content = failureContent,
                Round = session.RoundNumber,
                IsStreaming = false,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            await transcriptRepository.AppendAsync(sessionId, failureMessage, cancellationToken);
            await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, failureMessage, cancellationToken);

            return new SessionMessageResult(
                sessionId,
                session.Phase.ToString(),
                CanonicalizeAgentId(primaryAgentId),
                failureContent,
                PatchApplied: false);
        }
    }

    public Task<SessionState?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        sessionRepository.GetAsync(sessionId, cancellationToken);

    public Task<TruthMapState?> GetTruthMapAsync(Guid sessionId, CancellationToken cancellationToken) =>
        truthMapRepository.GetAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<TranscriptMessage>> GetTranscriptAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        transcriptRepository.GetBySessionAsync(sessionId, cancellationToken);

    private List<ICouncilAgent> ResolveAgentsForMessage(IReadOnlyList<string> preferredAgentIds)
    {
        var byCanonicalId = councilAgents
            .GroupBy(agent => CanonicalizeAgentId(agent.AgentId), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        if (byCanonicalId.Count == 0)
        {
            throw new InvalidOperationException("No council agents are configured.");
        }

        var resolved = new List<ICouncilAgent>();
        foreach (var agentId in preferredAgentIds)
        {
            var canonicalPreferred = CanonicalizeAgentId(agentId);
            if (byCanonicalId.TryGetValue(canonicalPreferred, out var preferred))
            {
                resolved.Add(preferred);
            }
        }

        if (resolved.Count > 0)
        {
            return resolved.Distinct().ToList();
        }

        return [byCanonicalId.Values.First()];
    }

    private static TranscriptMessage CreateRoundKickoffSystemMessage(Guid sessionId, int roundNumber, SessionPhase phase)
    {
        return new TranscriptMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Type = TranscriptMessageType.System,
            AgentId = null,
            Content = $"Phase {phase} started (round {roundNumber}). Dispatching council agents.",
            Round = roundNumber,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<bool> StreamAndStoreAgentMessageAsync(
        Guid sessionId,
        int roundNumber,
        AgentExecutionResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
        {
            return false;
        }

        var messageId = Guid.NewGuid();
        var segments = StreamingChunker.BuildSegments(result.Message);
        var agentId = PresentableAgentId(result.AgentId);

        for (var index = 0; index < segments.Count - 1; index++)
        {
            var partialMessage = new TranscriptMessage
            {
                Id = messageId,
                SessionId = sessionId,
                Type = TranscriptMessageType.Agent,
                AgentId = agentId,
                Content = segments[index],
                Round = roundNumber,
                IsStreaming = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, partialMessage, cancellationToken);

            // Small pacing delay so partial updates remain visibly incremental in the UI.
            await Task.Delay(StreamingChunkDelayMilliseconds, cancellationToken);
        }

        var finalMessage = new TranscriptMessage
        {
            Id = messageId,
            SessionId = sessionId,
            Type = TranscriptMessageType.Agent,
            AgentId = agentId,
            Content = segments[^1],
            Round = roundNumber,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await transcriptRepository.AppendAsync(sessionId, finalMessage, cancellationToken);
        await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, finalMessage, cancellationToken);
        return true;
    }

    private async Task<bool> StreamAndStoreTranscriptMessageAsync(
        Guid sessionId,
        int roundNumber,
        TranscriptMessage message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.Content))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(message.AgentId))
        {
            await transcriptRepository.AppendAsync(sessionId, message, cancellationToken);
            await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, message, cancellationToken);
            return true;
        }

        var messageId = message.Id == Guid.Empty ? Guid.NewGuid() : message.Id;
        var segments = StreamingChunker.BuildSegments(message.Content);

        for (var index = 0; index < segments.Count - 1; index++)
        {
            var partialMessage = new TranscriptMessage
            {
                Id = messageId,
                SessionId = sessionId,
                Type = message.Type,
                AgentId = message.AgentId,
                Content = segments[index],
                Round = roundNumber,
                IsStreaming = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, partialMessage, cancellationToken);
            await Task.Delay(StreamingChunkDelayMilliseconds, cancellationToken);
        }

        var finalMessage = new TranscriptMessage
        {
            Id = messageId,
            SessionId = sessionId,
            Type = message.Type,
            AgentId = message.AgentId,
            Content = segments[^1],
            Round = roundNumber,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await transcriptRepository.AppendAsync(sessionId, finalMessage, cancellationToken);
        await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, finalMessage, cancellationToken);
        return true;
    }

    private async Task StoreFinalTranscriptMessageAsync(
        Guid sessionId,
        TranscriptMessage message,
        CancellationToken cancellationToken)
    {
        await transcriptRepository.AppendAsync(sessionId, message, cancellationToken);
        await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, message, cancellationToken);
    }

    private sealed record ModeratorSummaryResult(TranscriptMessage Message, bool WasStreamed);
    private sealed record ModeratorSummaryRunResult(string Summary, bool WasStreamed, Guid MessageId);
    private sealed record StreamedSummaryResult(string Summary, bool WasStreamed, Guid MessageId);

    private async Task<ModeratorSummaryResult?> CreateModeratorSummaryMessageAsync(
        SessionState session,
        TruthMapState map,
        IReadOnlyList<AgentExecutionResult> results,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var successful = results
            .Where(result => !string.IsNullOrWhiteSpace(result.Message))
            .ToList();

        var summaryAgent = ResolveOptionalAgent(AgentId.Synthesizer);
        var summaryResult = summaryAgent is null
            ? new ModeratorSummaryRunResult(BuildFallbackModeratorSummary(successful), false, Guid.NewGuid())
            : await TryBuildAgentModeratorSummaryAsync(
                summaryAgent,
                session,
                map,
                successful,
                correlationId,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(summaryResult.Summary))
        {
            return null;
        }

        var structuredSummary = EnsureModeratorSummaryStructure(summaryResult.Summary, successful);
        var enrichedSummary = EnsureAgentSummaries(structuredSummary, successful);

        return new ModeratorSummaryResult(new TranscriptMessage
        {
            Id = summaryResult.MessageId,
            SessionId = session.SessionId,
            Type = TranscriptMessageType.Agent,
            AgentId = PresentableAgentId(AgentId.Synthesizer),
            Content = enrichedSummary,
            Round = session.RoundNumber,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        }, summaryResult.WasStreamed);
    }

    private async Task<ModeratorSummaryRunResult> TryBuildAgentModeratorSummaryAsync(
        ICouncilAgent summaryAgent,
        SessionState session,
        TruthMapState map,
        IReadOnlyList<AgentExecutionResult> successful,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var councilMessagePreview = successful.Count == 0
            ? "No agent responses were captured in this round."
            : string.Join(
                "\n\n",
                successful.Select(result =>
                    $"### {PresentableAgentId(result.AgentId)}\n{TruncateForDetailedSummary(result.Message, 900)}"));

        var context = new AgentContext
        {
            SessionId = session.SessionId,
            CorrelationId = correlationId,
            Round = session.RoundNumber,
            Phase = SessionPhase.PostDelivery,
            FrictionLevel = session.FrictionLevel,
            TruthMap = map.DeepCopy(),
            MicroDirectives =
            [
                "Produce a detailed moderator summary using this exact Markdown structure:",
                "## Moderator summary",
                "### Executive synthesis",
                "### Key recommendations",
                "### Agent summaries",
                "### Key tensions to challenge",
                "### Suggested next user prompts",
                "Use short paragraphs and bullet lists; in Agent summaries, include 3-5 bullets per agent.",
                "Aim for 450-650 words. Be specific and grounded in the agent responses.",
                $"Council response snapshots:\n{councilMessagePreview}"
            ]
        };

        try
        {
            var streamed = await StreamModeratorSummaryAsync(
                session,
                summaryAgent,
                context,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(streamed.Summary))
            {
                return new ModeratorSummaryRunResult(streamed.Summary.Trim(), streamed.WasStreamed, streamed.MessageId);
            }

            var response = await summaryAgent.RunAsync(context, cancellationToken);
            if (!string.IsNullOrWhiteSpace(response.Message))
            {
                var fallbackId = Guid.NewGuid();
                await StreamSummaryFromTextAsync(
                    session,
                    response.Message.Trim(),
                    fallbackId,
                    cancellationToken);
                return new ModeratorSummaryRunResult(response.Message.Trim(), true, fallbackId);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to generate moderator summary via synthesis agent. SessionId={SessionId} Round={Round} CorrelationId={CorrelationId}",
                session.SessionId,
                session.RoundNumber,
                correlationId);
        }

        return new ModeratorSummaryRunResult(BuildFallbackModeratorSummary(successful), false, Guid.NewGuid());
    }

    private async Task<StreamedSummaryResult> StreamModeratorSummaryAsync(
        SessionState session,
        ICouncilAgent summaryAgent,
        AgentContext context,
        CancellationToken cancellationToken)
    {
        var buffer = new System.Text.StringBuilder();
        var messageId = Guid.NewGuid();
        var agentId = PresentableAgentId(AgentId.Synthesizer);
        var lastEmissionLength = 0;
        var emitted = false;

        await foreach (var chunk in summaryAgent.RunStreamingAsync(context, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            buffer.Append(chunk);
            var segments = StreamingChunker.BuildSegments(buffer.ToString());
            foreach (var segment in segments)
            {
                if (segment.Length <= lastEmissionLength)
                {
                    continue;
                }

                lastEmissionLength = segment.Length;
                var partialMessage = new TranscriptMessage
                {
                    Id = messageId,
                    SessionId = session.SessionId,
                    Type = TranscriptMessageType.Agent,
                    AgentId = agentId,
                    Content = segment.TrimEnd(),
                    Round = session.RoundNumber,
                    IsStreaming = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                await eventBroadcaster.TranscriptMessageAppendedAsync(session.SessionId, partialMessage, cancellationToken);
                emitted = true;
                await Task.Delay(StreamingChunkDelayMilliseconds, cancellationToken);
            }
        }

        return new StreamedSummaryResult(buffer.ToString(), emitted, messageId);
    }

    private async Task StreamSummaryFromTextAsync(
        SessionState session,
        string summary,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var segments = StreamingChunker.BuildSegments(summary);
        if (segments.Count <= 1)
        {
            return;
        }

        for (var index = 0; index < segments.Count - 1; index++)
        {
            var partialMessage = new TranscriptMessage
            {
                Id = messageId,
                SessionId = session.SessionId,
                Type = TranscriptMessageType.Agent,
                AgentId = PresentableAgentId(AgentId.Synthesizer),
                Content = segments[index],
                Round = session.RoundNumber,
                IsStreaming = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            await eventBroadcaster.TranscriptMessageAppendedAsync(session.SessionId, partialMessage, cancellationToken);
            await Task.Delay(StreamingChunkDelayMilliseconds, cancellationToken);
        }
    }

    private static string BuildFallbackModeratorSummary(IReadOnlyList<AgentExecutionResult> successful)
    {
        if (successful.Count == 0)
        {
            return """
                ## Moderator summary
                ### Executive synthesis
                No agent analysis was captured in this round.

                ### Key recommendations
                - Ask the moderator to rerun with a narrower question.
                - Clarify your constraints (budget, timeline, and target user).
                - Challenge one critical assumption to unblock progress.

                ### Agent summaries
                No agent summaries available.

                ### Key tensions to challenge
                - Which assumption would invalidate the plan if wrong?
                - What would make this effort too expensive for the expected upside?

                ### Suggested next user prompts
                1. "What is the smallest validation step we can run this week?"
                2. "Which assumption is the riskiest to leave untested?"
                3. "What should we remove to reduce complexity?"
                """;
        }

        var consolidated = string.Join(
            "\n",
            successful.Select(result =>
                $"- **{PresentableAgentId(result.AgentId)}**: {TruncateForSummary(result.Message, 220)}"));
        var agentSummaries = BuildAgentSummarySection(successful);

        return $$"""
            ## Moderator summary
            ### Executive synthesis
            The council provided {{successful.Count}} perspectives. The core recommendations and tradeoffs are captured below.

            ### Key recommendations
            {{consolidated}}

            ### Agent summaries
            {{agentSummaries}}

            ### Key tensions to challenge
            - Which recommendation is highest impact but highest risk?
            - Which assumption, if false, invalidates most of the plan?
            - Which agent recommendations conflict most strongly?

            ### Suggested next user prompts
            1. "Challenge the weakest assumption in this plan."
            2. "What is the fastest low-risk validation experiment?"
            3. "What should we de-scope immediately?"
            """;
    }

    private static string EnsureModeratorSummaryStructure(
        string summary,
        IReadOnlyList<AgentExecutionResult> successful)
    {
        var trimmed = summary.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        var hasHeadings = Regex.IsMatch(trimmed, @"^#{2,3}\s", RegexOptions.Multiline);
        if (!hasHeadings)
        {
            var recommendations = BuildRecommendationSection(successful);
            return $$"""
                ## Moderator summary
                ### Executive synthesis
                {{trimmed}}

                ### Key recommendations
                {{recommendations}}

                ### Key tensions to challenge
                - Which recommendation is highest impact but highest risk?
                - Which assumption, if false, invalidates most of the plan?
                - Which agent recommendations conflict most strongly?

                ### Suggested next user prompts
                1. "What is the riskiest assumption to test first?"
                2. "Which recommendation should we validate with a cheap experiment?"
                3. "What is the smallest decision we can make today?"
                """;
        }

        if (Regex.IsMatch(trimmed, @"^#{2,3}\s+Moderator summary", RegexOptions.IgnoreCase))
        {
            return trimmed;
        }

        return $$"""
            ## Moderator summary
            {{trimmed}}
            """;
    }

    private static string EnsureAgentSummaries(
        string summary,
        IReadOnlyList<AgentExecutionResult> successful)
    {
        if (summary.Contains("Agent summaries", StringComparison.OrdinalIgnoreCase))
        {
            return summary;
        }

        var agentSummaries = BuildAgentSummarySection(successful);
        if (string.IsNullOrWhiteSpace(agentSummaries))
        {
            return summary;
        }

        return $$"""
            {{summary.TrimEnd()}}

            ### Agent summaries
            {{agentSummaries}}
            """;
    }

    private static string BuildAgentSummarySection(IReadOnlyList<AgentExecutionResult> successful)
    {
        if (successful.Count == 0)
        {
            return "No agent summaries available.";
        }

        return string.Join(
            "\n\n",
            successful.Select(result =>
                $$"""
                #### {{PresentableAgentId(result.AgentId)}}
                {{BuildBulletedSummary(result.Message)}}
                """.Trim()));
    }

    private static string BuildRecommendationSection(IReadOnlyList<AgentExecutionResult> successful)
    {
        if (successful.Count == 0)
        {
            return "- No recommendations were captured.";
        }

        var recommendations = successful
            .Where(result => !string.IsNullOrWhiteSpace(result.Message))
            .Select(result =>
                $"- **{PresentableAgentId(result.AgentId)}**: {TruncateForSummary(result.Message, 220)}")
            .ToList();

        if (recommendations.Count == 0)
        {
            return "- No recommendations were captured.";
        }

        return string.Join("\n", recommendations);
    }

    private static string BuildBulletedSummary(string? message)
    {
        var bullets = ExtractBulletCandidates(message, maxBullets: 5);
        if (bullets.Count == 0)
        {
            return "- No summary available.";
        }

        return string.Join("\n", bullets.Select(item => $"- {item.Trim()}"));
    }

    private static IReadOnlyList<string> ExtractBulletCandidates(string? message, int maxBullets)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return [];
        }

        var normalized = NormalizeForBulletExtraction(message, 8000);

        var lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToList();

        var existingBullets = lines
            .Where(line =>
                line.StartsWith("- ", StringComparison.Ordinal)
                || line.StartsWith("* ", StringComparison.Ordinal)
                || Regex.IsMatch(line, @"^\d+\.\s", RegexOptions.None))
            .Select(StripListMarker)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(maxBullets)
            .ToList();

        if (existingBullets.Count > 0)
        {
            return existingBullets;
        }

        var sentences = Regex
            .Split(normalized, @"(?<=[.!?])\s+")
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0)
            .Take(maxBullets)
            .ToList();

        return sentences;
    }

    private static string StripListMarker(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            return trimmed[2..].Trim();
        }

        var numbered = Regex.Match(trimmed, @"^\d+\.\s+(.*)$");
        return numbered.Success ? numbered.Groups[1].Value.Trim() : trimmed;
    }

    private ICouncilAgent? ResolveOptionalAgent(string agentId)
    {
        var canonicalAgentId = CanonicalizeAgentId(agentId);
        return councilAgents.FirstOrDefault(
            agent => CanonicalizeAgentId(agent.AgentId) == canonicalAgentId);
    }


    private List<ICouncilAgent> SelectActiveAgentsForPhase(SessionPhase phase)
    {
        var byId = AgentConfig.DefaultCouncil.ToDictionary(config => config.AgentId, StringComparer.Ordinal);

        return councilAgents
            .Where(agent =>
            {
                var canonicalAgentId = CanonicalizeAgentId(agent.AgentId);
                return byId.TryGetValue(canonicalAgentId, out var config)
                    && config.ActivePhases.Contains(phase);
            })
            .ToList();
    }

    private static TimeSpan ResolveRoundTimeout(IReadOnlyList<ICouncilAgent> activeAgents)
    {
        var byId = AgentConfig.DefaultCouncil.ToDictionary(config => config.AgentId, StringComparer.Ordinal);
        var timeoutSeconds = activeAgents
            .Select(agent => CanonicalizeAgentId(agent.AgentId))
            .Where(byId.ContainsKey)
            .Select(agentId => byId[agentId].TimeoutSeconds)
            .DefaultIfEmpty(90)
            .Max();

        return TimeSpan.FromSeconds(timeoutSeconds);
    }

    private static TranscriptMessage? CreateTranscriptMessageForResult(
        Guid sessionId,
        int roundNumber,
        AgentExecutionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            return new TranscriptMessage
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                Type = TranscriptMessageType.Agent,
                AgentId = PresentableAgentId(result.AgentId),
                Content = result.Message,
                Round = roundNumber,
                IsStreaming = false,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        if (result.TimedOut)
        {
            return new TranscriptMessage
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                Type = TranscriptMessageType.System,
                Content = $"Agent '{PresentableAgentId(result.AgentId)}' timed out in round {roundNumber}.",
                Round = roundNumber,
                IsStreaming = false,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return new TranscriptMessage
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                Type = TranscriptMessageType.System,
                Content = $"Agent '{PresentableAgentId(result.AgentId)}' failed in round {roundNumber}. Reason: {TrimError(result.Error)}",
                Round = roundNumber,
                IsStreaming = false,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        return null;
    }

    private static TranscriptMessage CreateRoundCompletionSystemMessage(
        Guid sessionId,
        int roundNumber,
        IReadOnlyList<AgentExecutionResult> results,
        int streamedMessageCount)
    {
        var completedAgents = results.Count(result => !string.IsNullOrWhiteSpace(result.Message));
        var failedAgents = results.Count(result => result.Error is not null && result.Error != "timeout");
        var timedOutAgents = results.Count(result => result.TimedOut);
        var nextStep = "Next step: ask the Council Moderator one focused follow-up question to drive the next council action.";

        return new TranscriptMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Type = TranscriptMessageType.System,
            Content = $"Round {roundNumber} complete. Responses={completedAgents}, Failures={failedAgents}, Timeouts={timedOutAgents}, StreamedMessages={streamedMessageCount}. {nextStep}",
            Round = roundNumber,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string CanonicalizeAgentId(string agentId) =>
        (agentId ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");

    private static string PresentableAgentId(string agentId) =>
        CanonicalizeAgentId(agentId).Replace("_", "-");

    private static string TrimError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "unknown";
        }

        var trimmed = error.Trim();
        return trimmed.Length <= 180
            ? trimmed
            : trimmed[..180];
    }

    private static string TruncateForSummary(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "No content provided.";
        }

        var normalized = string.Join(
            " ",
            text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Trim();

        return TruncateAtWordBoundary(normalized, maxLength);
    }

    private static string TruncateForDetailedSummary(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "No content provided.";
        }

        var normalized = text.Trim();
        return TruncateAtWordBoundary(normalized, maxLength);
    }

    private static string NormalizeForBulletExtraction(string message, int maxLength)
    {
        var normalized = (message ?? string.Empty)
            .Replace("\r", string.Empty)
            .Trim();

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var candidate = normalized[..maxLength];
        var lastLineBreak = candidate.LastIndexOf('\n');
        if (lastLineBreak > 0)
        {
            return candidate[..lastLineBreak].TrimEnd();
        }

        var lastSpace = candidate.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            return candidate[..lastSpace].TrimEnd();
        }

        return candidate.TrimEnd();
    }

    private static string TruncateAtWordBoundary(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var safeLength = Math.Min(maxLength, text.Length);
        var candidate = text[..safeLength].TrimEnd();
        var lastSpace = candidate.LastIndexOf(' ');
        if (lastSpace > Math.Max(24, maxLength / 2))
        {
            candidate = candidate[..lastSpace].TrimEnd();
        }

        return $"{candidate}...";
    }

    private static string NormalizeCorrelationId(string? correlationId) =>
        string.IsNullOrWhiteSpace(correlationId) ? "n/a" : correlationId.Trim();

    private static IReadOnlyList<string> ResolveRoutedAgentIds(SessionPhase phase, string message)
    {
        return phase switch
        {
            SessionPhase.Clarification => [AgentId.Moderator],
            SessionPhase.PostDelivery => RoutePostDeliveryMessage(message),
            _ => throw new InvalidOperationException(
                $"Messages are not accepted during phase '{phase}'. Allowed phases: Clarification, PostDelivery.")
        };
    }

    private static IReadOnlyList<string> RoutePostDeliveryMessage(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();

        // In the simplified architecture, we route to the most appropriate working agent
        // based on the message content. All three agents can handle any topic but have
        // different perspectives (GPT = initial analysis, Gemini = improvement, Claude = refinement).
        
        if (ContainsAny(normalized, "architecture", "technical", "stack", "infra", "latency", "performance", "scalability"))
        {
            // Technical questions → GPT (broad analysis) + Claude (nuanced refinement)
            return [AgentId.GptAgent, AgentId.ClaudeAgent];
        }

        if (ContainsAny(normalized, "risk", "threat", "failure", "concern", "red team"))
        {
            // Risk questions → Gemini (critical perspective) + Claude (balanced view)
            return [AgentId.GeminiAgent, AgentId.ClaudeAgent];
        }

        if (ContainsAny(normalized, "research", "evidence", "source", "citation", "data"))
        {
            // Research questions → GPT (comprehensive) + Gemini (alternative perspectives)
            return [AgentId.GptAgent, AgentId.GeminiAgent];
        }

        if (ContainsAny(normalized, "summar", "synthes", "recap"))
        {
            return [AgentId.Synthesizer];
        }

        if (ContainsAny(normalized, "clarify", "assumption", "question", "unclear"))
        {
            return [AgentId.Moderator, AgentId.GptAgent];
        }

        // Default: GPT for comprehensive analysis, Gemini for alternative perspective
        return [AgentId.GptAgent, AgentId.GeminiAgent];
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.Ordinal));

    private static IReadOnlyList<string> BuildFollowUpDirectives(
        string message,
        IReadOnlyList<string> routedAgentIds)
    {
        var directives = new List<string>
        {
            $"User message: {message}"
        };

        if (ShouldAnswerTechnically(message, routedAgentIds))
        {
            directives.Add("Respond with technical depth. Include specific technologies, architecture components, data model, deployment, and trade-offs. Avoid high-level product-only advice.");
        }

        return directives;
    }

    private static bool ShouldAnswerTechnically(string message, IReadOnlyList<string> routedAgentIds)
    {
        var normalized = message.Trim().ToLowerInvariant();
        if (routedAgentIds.Any(agentId =>
                CanonicalizeAgentId(agentId) == CanonicalizeAgentId(AgentId.GptAgent) ||
                CanonicalizeAgentId(agentId) == CanonicalizeAgentId(AgentId.ClaudeAgent)))
        {
            return true;
        }

        return ContainsAny(
            normalized,
            "tech",
            "stack",
            "architecture",
            "infra",
            "database",
            "db",
            "latency",
            "performance",
            "scalability",
            "api",
            "backend",
            "deployment");
    }
}
