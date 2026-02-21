using System.Collections.Concurrent;
using Agon.Application.Interfaces;
using Agon.Application.Sessions;

namespace Agon.Infrastructure.Persistence.InMemory;

public class InMemorySessionRepository : ISessionRepository
{
    private readonly ConcurrentDictionary<Guid, SessionState> sessions = new();

    public Task CreateAsync(SessionState session, CancellationToken cancellationToken)
    {
        sessions[session.SessionId] = Clone(session);
        return Task.CompletedTask;
    }

    public Task<SessionState?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session is null ? null : Clone(session));
    }

    public Task UpdateAsync(SessionState session, CancellationToken cancellationToken)
    {
        sessions[session.SessionId] = Clone(session);
        return Task.CompletedTask;
    }

    private static SessionState Clone(SessionState session)
    {
        return new SessionState
        {
            SessionId = session.SessionId,
            Mode = session.Mode,
            Status = session.Status,
            Phase = session.Phase,
            FrictionLevel = session.FrictionLevel,
            RoundPolicy = new()
            {
                MaxClarificationRounds = session.RoundPolicy.MaxClarificationRounds,
                MaxDebateRounds = session.RoundPolicy.MaxDebateRounds,
                MaxTargetedLoops = session.RoundPolicy.MaxTargetedLoops,
                MaxSessionBudgetTokens = session.RoundPolicy.MaxSessionBudgetTokens,
                ConvergenceThresholdStandard = session.RoundPolicy.ConvergenceThresholdStandard,
                ConvergenceThresholdHighFriction = session.RoundPolicy.ConvergenceThresholdHighFriction,
                HighFrictionCutoff = session.RoundPolicy.HighFrictionCutoff
            },
            RoundNumber = session.RoundNumber,
            TargetedLoopCount = session.TargetedLoopCount,
            TokensUsed = session.TokensUsed,
            ClarificationIncomplete = session.ClarificationIncomplete
        };
    }
}
