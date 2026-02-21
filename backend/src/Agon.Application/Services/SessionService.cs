using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;
using Microsoft.Extensions.Logging;

namespace Agon.Application.Services;

public class SessionService(
    ISessionRepository sessionRepository,
    ITruthMapRepository truthMapRepository,
    Orchestrator orchestrator,
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
}
