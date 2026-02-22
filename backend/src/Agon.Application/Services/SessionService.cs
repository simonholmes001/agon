using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Sessions;
using Agon.Domain.Agents;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Microsoft.Extensions.Logging;

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

    public Task<SessionState?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        sessionRepository.GetAsync(sessionId, cancellationToken);

    public Task<TruthMapState?> GetTruthMapAsync(Guid sessionId, CancellationToken cancellationToken) =>
        truthMapRepository.GetAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<TranscriptMessage>> GetTranscriptAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        transcriptRepository.GetBySessionAsync(sessionId, cancellationToken);

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

    private static string NormalizeCorrelationId(string? correlationId) =>
        string.IsNullOrWhiteSpace(correlationId) ? "n/a" : correlationId.Trim();
}
