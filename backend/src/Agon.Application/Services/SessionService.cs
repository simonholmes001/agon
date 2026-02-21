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

    public async Task<SessionState> StartSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetAsync(sessionId, cancellationToken);
        if (session is null)
        {
            logger.LogWarning("Cannot start session because it was not found. SessionId={SessionId}", sessionId);
            throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
        }

        var map = await truthMapRepository.GetAsync(sessionId, cancellationToken);
        if (map is null)
        {
            logger.LogWarning("Cannot start session because truth map was not found. SessionId={SessionId}", sessionId);
            throw new KeyNotFoundException($"Truth map for session '{sessionId}' was not found.");
        }

        if (session.Phase == SessionPhase.Clarification)
        {
            orchestrator.StartDebateRound1(session);
            map.Round = session.RoundNumber;
            await truthMapRepository.UpdateAsync(map, cancellationToken);
            await eventBroadcaster.RoundProgressAsync(sessionId, session.Phase, cancellationToken);
            await transcriptRepository.AppendAsync(
                sessionId,
                CreateRoundKickoffSystemMessage(sessionId, session.RoundNumber),
                cancellationToken);

            await RunCouncilRoundAsync(session, map, cancellationToken);
        }
        else
        {
            logger.LogDebug(
                "StartSession called but session is not in clarification. SessionId={SessionId} Phase={Phase}",
                sessionId,
                session.Phase);
        }

        await sessionRepository.UpdateAsync(session, cancellationToken);
        logger.LogInformation(
            "Started session. SessionId={SessionId} Phase={Phase} Round={Round}",
            sessionId,
            session.Phase,
            session.RoundNumber);
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
        CancellationToken cancellationToken)
    {
        var activeAgents = SelectActiveAgentsForPhase(session.Phase);
        if (activeAgents.Count == 0)
        {
            logger.LogWarning(
                "No active council agents configured for phase. SessionId={SessionId} Phase={Phase}",
                session.SessionId,
                session.Phase);
            return;
        }

        var context = new AgentContext
        {
            SessionId = session.SessionId,
            Round = session.RoundNumber,
            Phase = session.Phase,
            FrictionLevel = session.FrictionLevel,
            TruthMap = map.DeepCopy()
        };

        var timeout = ResolveRoundTimeout(activeAgents);
        logger.LogInformation(
            "Dispatching council round. SessionId={SessionId} Round={Round} Phase={Phase} AgentCount={AgentCount} TimeoutSeconds={TimeoutSeconds}",
            session.SessionId,
            session.RoundNumber,
            session.Phase,
            activeAgents.Count,
            timeout.TotalSeconds);

        var results = await agentRunner.RunRoundAsync(
            activeAgents,
            context,
            timeout,
            cancellationToken);

        var storedMessages = await AppendRoundTranscriptMessagesAsync(
            session.SessionId,
            session.RoundNumber,
            results,
            cancellationToken);

        await agentRunner.ApplyValidatedPatchesAsync(session.SessionId, results, cancellationToken);

        logger.LogInformation(
            "Council round completed. SessionId={SessionId} Round={Round} AgentCount={AgentCount} StoredMessages={StoredMessages} TimedOut={TimedOut} Failed={Failed}",
            session.SessionId,
            session.RoundNumber,
            activeAgents.Count,
            storedMessages,
            results.Count(result => result.TimedOut),
            results.Count(result => result.Error is not null && result.Error != "timeout"));
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

    private async Task<int> AppendRoundTranscriptMessagesAsync(
        Guid sessionId,
        int roundNumber,
        IEnumerable<AgentExecutionResult> results,
        CancellationToken cancellationToken)
    {
        var stored = 0;
        foreach (var result in results)
        {
            TranscriptMessage? transcriptMessage = null;

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                transcriptMessage = new TranscriptMessage
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
            else if (result.TimedOut)
            {
                transcriptMessage = new TranscriptMessage
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
            else if (!string.IsNullOrWhiteSpace(result.Error))
            {
                transcriptMessage = new TranscriptMessage
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

            if (transcriptMessage is null) continue;
            await transcriptRepository.AppendAsync(sessionId, transcriptMessage, cancellationToken);
            stored++;
        }

        return stored;
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
}
