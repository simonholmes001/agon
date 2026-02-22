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
    private const int StreamingChunkDelayMilliseconds = 120;

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
        var session = await sessionRepository.GetAsync(sessionId, cancellationToken);
        if (session is null)
        {
            logger.LogWarning(
                "Cannot start session because it was not found. SessionId={SessionId} CorrelationId={CorrelationId}",
                sessionId,
                resolvedCorrelationId);
            throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
        }

        var map = await truthMapRepository.GetAsync(sessionId, cancellationToken);
        if (map is null)
        {
            logger.LogWarning(
                "Cannot start session because truth map was not found. SessionId={SessionId} CorrelationId={CorrelationId}",
                sessionId,
                resolvedCorrelationId);
            throw new KeyNotFoundException($"Truth map for session '{sessionId}' was not found.");
        }

        if (session.Phase == SessionPhase.Clarification)
        {
            orchestrator.StartDebateRound1(session);
            map.Round = session.RoundNumber;
            await truthMapRepository.UpdateAsync(map, cancellationToken);
            await eventBroadcaster.RoundProgressAsync(sessionId, session.Phase, cancellationToken);
            var kickoffMessage = CreateRoundKickoffSystemMessage(sessionId, session.RoundNumber);
            await transcriptRepository.AppendAsync(sessionId, kickoffMessage, cancellationToken);
            await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, kickoffMessage, cancellationToken);

            await RunCouncilRoundAsync(session, map, resolvedCorrelationId, cancellationToken);

            // Current vertical slice completes after round 1 and opens
            // a moderator follow-up lane for user-guided orchestration.
            session.Phase = SessionPhase.PostDelivery;
            session.Status = SessionStatus.CompleteWithGaps;
            await eventBroadcaster.RoundProgressAsync(sessionId, session.Phase, cancellationToken);
        }
        else
        {
            logger.LogDebug(
                "StartSession called but session is not in clarification. SessionId={SessionId} Phase={Phase} CorrelationId={CorrelationId}",
                sessionId,
                session.Phase,
                resolvedCorrelationId);
        }

        await sessionRepository.UpdateAsync(session, cancellationToken);
        logger.LogInformation(
            "Started session. SessionId={SessionId} Phase={Phase} Round={Round} CorrelationId={CorrelationId}",
            sessionId,
            session.Phase,
            session.RoundNumber,
            resolvedCorrelationId);
        return session;
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

            if (results.Count > 1)
            {
                var summaryMessage = await CreateModeratorSummaryMessageAsync(
                    session,
                    map,
                    results,
                    resolvedCorrelationId,
                    cancellationToken);
                if (summaryMessage is not null)
                {
                    await transcriptRepository.AppendAsync(sessionId, summaryMessage, cancellationToken);
                    await eventBroadcaster.TranscriptMessageAppendedAsync(sessionId, summaryMessage, cancellationToken);
                    storedMessages++;
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

    private static TranscriptMessage CreateRoundKickoffSystemMessage(Guid sessionId, int roundNumber)
    {
        return new TranscriptMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Type = TranscriptMessageType.System,
            AgentId = null,
            Content = $"Round {roundNumber} started. Dispatching council agents.",
            Round = roundNumber,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task RunCouncilRoundAsync(
        SessionState session,
        TruthMapState map,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var activeAgents = SelectActiveAgentsForPhase(session.Phase);
        if (activeAgents.Count == 0)
        {
            logger.LogWarning(
                "No active council agents configured for phase. SessionId={SessionId} Phase={Phase} CorrelationId={CorrelationId}",
                session.SessionId,
                session.Phase,
                correlationId);
            return;
        }

        var context = new AgentContext
        {
            SessionId = session.SessionId,
            CorrelationId = correlationId,
            Round = session.RoundNumber,
            Phase = session.Phase,
            FrictionLevel = session.FrictionLevel,
            TruthMap = map.DeepCopy()
        };

        var timeout = ResolveRoundTimeout(activeAgents);
        logger.LogInformation(
            "Dispatching council round. SessionId={SessionId} Round={Round} Phase={Phase} AgentCount={AgentCount} TimeoutSeconds={TimeoutSeconds} CorrelationId={CorrelationId}",
            session.SessionId,
            session.RoundNumber,
            session.Phase,
            activeAgents.Count,
            timeout.TotalSeconds,
            correlationId);

        var storedMessages = 0;
        var results = await agentRunner.RunRoundAsync(
            activeAgents,
            context,
            timeout,
            async (result, token) =>
            {
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
                    return;
                }

                await transcriptRepository.AppendAsync(session.SessionId, transcriptMessage, token);
                await eventBroadcaster.TranscriptMessageAppendedAsync(session.SessionId, transcriptMessage, token);
                storedMessages++;
            },
            cancellationToken);

        var summaryMessage = await CreateModeratorSummaryMessageAsync(
            session,
            map,
            results,
            correlationId,
            cancellationToken);
        if (summaryMessage is not null)
        {
            await transcriptRepository.AppendAsync(session.SessionId, summaryMessage, cancellationToken);
            await eventBroadcaster.TranscriptMessageAppendedAsync(session.SessionId, summaryMessage, cancellationToken);
            storedMessages++;
        }

        var completionMessage = CreateRoundCompletionSystemMessage(
            session.SessionId,
            session.RoundNumber,
            results,
            storedMessages);
        await transcriptRepository.AppendAsync(session.SessionId, completionMessage, cancellationToken);
        await eventBroadcaster.TranscriptMessageAppendedAsync(session.SessionId, completionMessage, cancellationToken);
        storedMessages++;

        await agentRunner.ApplyValidatedPatchesAsync(session.SessionId, results, cancellationToken);

        logger.LogInformation(
            "Council round completed. SessionId={SessionId} Round={Round} AgentCount={AgentCount} StoredMessages={StoredMessages} TimedOut={TimedOut} Failed={Failed} CorrelationId={CorrelationId}",
            session.SessionId,
            session.RoundNumber,
            activeAgents.Count,
            storedMessages,
            results.Count(result => result.TimedOut),
            results.Count(result => result.Error is not null && result.Error != "timeout"),
            correlationId);
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
        var segments = BuildStreamingSegments(result.Message);
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

    private async Task<TranscriptMessage?> CreateModeratorSummaryMessageAsync(
        SessionState session,
        TruthMapState map,
        IReadOnlyList<AgentExecutionResult> results,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var successful = results
            .Where(result => !string.IsNullOrWhiteSpace(result.Message))
            .ToList();

        var summaryAgent = ResolveOptionalAgent(AgentId.SynthesisValidation);
        var summary = summaryAgent is null
            ? BuildFallbackModeratorSummary(successful)
            : await TryBuildAgentModeratorSummaryAsync(
                summaryAgent,
                session,
                map,
                successful,
                correlationId,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var structuredSummary = EnsureModeratorSummaryStructure(summary, successful);
        var enrichedSummary = EnsureAgentSummaries(structuredSummary, successful);

        return new TranscriptMessage
        {
            Id = Guid.NewGuid(),
            SessionId = session.SessionId,
            Type = TranscriptMessageType.Agent,
            AgentId = PresentableAgentId(AgentId.SynthesisValidation),
            Content = enrichedSummary,
            Round = session.RoundNumber,
            IsStreaming = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<string> TryBuildAgentModeratorSummaryAsync(
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
            var response = await summaryAgent.RunAsync(context, cancellationToken);
            if (!string.IsNullOrWhiteSpace(response.Message))
            {
                return response.Message.Trim();
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

        return BuildFallbackModeratorSummary(successful);
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

        return string.Join("\n", bullets.Select(item => $"- {TruncateForSummary(item, 240)}"));
    }

    private static IReadOnlyList<string> ExtractBulletCandidates(string? message, int maxBullets)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return [];
        }

        var normalized = TruncateForDetailedSummary(message, 1200)
            .Replace("\r", string.Empty)
            .Trim();

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

    private static List<string> BuildStreamingSegments(string message)
    {
        var trimmed = message.Trim();
        if (trimmed.Length == 0)
        {
            return [string.Empty];
        }

        var words = trimmed
            .Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 6)
        {
            if (trimmed.Length < 40)
            {
                return [trimmed];
            }

            var midpoint = Math.Max(1, words.Length / 2);
            var partial = string.Join(" ", words.Take(midpoint));
            return [partial, trimmed];
        }

        var segmentCount = Math.Clamp(words.Length / 8, 2, 8);
        var segmentWordCount = Math.Max(4, words.Length / segmentCount);
        var segments = new List<string>();
        for (var end = segmentWordCount; end < words.Length; end += segmentWordCount)
        {
            segments.Add(string.Join(" ", words.Take(end)));
        }

        if (segments.Count == 0 || !segments[^1].Equals(trimmed, StringComparison.Ordinal))
        {
            segments.Add(trimmed);
        }

        if (segments.Count == 1 && trimmed.Length >= 60)
        {
            var midpoint = Math.Max(1, words.Length / 2);
            segments =
            [
                string.Join(" ", words.Take(midpoint)),
                trimmed
            ];
        }

        return segments;
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

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return $"{normalized[..maxLength].TrimEnd()}...";
    }

    private static string TruncateForDetailedSummary(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "No content provided.";
        }

        var normalized = text.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return $"{normalized[..maxLength].TrimEnd()}...";
    }

    private static string NormalizeCorrelationId(string? correlationId) =>
        string.IsNullOrWhiteSpace(correlationId) ? "n/a" : correlationId.Trim();

    private static IReadOnlyList<string> ResolveRoutedAgentIds(SessionPhase phase, string message)
    {
        return phase switch
        {
            SessionPhase.Clarification => [AgentId.SocraticClarifier],
            SessionPhase.PostDelivery => RoutePostDeliveryMessage(message),
            _ => throw new InvalidOperationException(
                $"Messages are not accepted during phase '{phase}'. Allowed phases: Clarification, PostDelivery.")
        };
    }

    private static IReadOnlyList<string> RoutePostDeliveryMessage(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();

        if (ContainsAny(normalized, "architecture", "technical", "stack", "infra", "latency", "performance", "scalability"))
        {
            return [AgentId.TechnicalArchitect, AgentId.Contrarian];
        }

        if (ContainsAny(normalized, "risk", "threat", "failure", "concern", "red team"))
        {
            return [AgentId.Contrarian, AgentId.TechnicalArchitect];
        }

        if (ContainsAny(normalized, "research", "evidence", "source", "citation", "data"))
        {
            return [AgentId.ResearchLibrarian, AgentId.ProductStrategist];
        }

        if (ContainsAny(normalized, "summar", "synthes", "recap"))
        {
            return [AgentId.SynthesisValidation];
        }

        if (ContainsAny(normalized, "clarify", "assumption", "question", "unclear"))
        {
            return [AgentId.SocraticClarifier, AgentId.ProductStrategist];
        }

        return [AgentId.ProductStrategist, AgentId.Contrarian];
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
                CanonicalizeAgentId(agentId) == CanonicalizeAgentId(AgentId.TechnicalArchitect)))
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
