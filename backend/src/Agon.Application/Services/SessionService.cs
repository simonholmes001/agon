using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Agon.Application.Sessions;
using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;

namespace Agon.Application.Services;

public class SessionService(
    ISessionRepository sessionRepository,
    ITruthMapRepository truthMapRepository,
    Orchestrator orchestrator)
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
            throw new ArgumentException("Idea must be at least 10 non-whitespace characters.", nameof(idea));
        }

        if (frictionLevel is < 0 or > 100)
        {
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

        return session;
    }

    public async Task<SessionState> StartSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetAsync(sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session '{sessionId}' was not found.");

        var map = await truthMapRepository.GetAsync(sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Truth map for session '{sessionId}' was not found.");

        if (session.Phase == SessionPhase.Clarification)
        {
            orchestrator.StartDebateRound1(session);
            map.Round = session.RoundNumber;
            await truthMapRepository.UpdateAsync(map, cancellationToken);
        }

        await sessionRepository.UpdateAsync(session, cancellationToken);
        return session;
    }

    public Task<SessionState?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        sessionRepository.GetAsync(sessionId, cancellationToken);

    public Task<TruthMapState?> GetTruthMapAsync(Guid sessionId, CancellationToken cancellationToken) =>
        truthMapRepository.GetAsync(sessionId, cancellationToken);
}
