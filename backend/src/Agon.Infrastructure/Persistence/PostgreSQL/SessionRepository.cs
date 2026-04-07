using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Infrastructure.Persistence.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of ISessionRepository using EF Core.
/// Stores session lifecycle state (phase, status, counters) — not the Truth Map itself.
/// </summary>
public sealed class SessionRepository : ISessionRepository
{
    private readonly AgonDbContext _dbContext;
    private readonly ITruthMapRepository _truthMapRepo;

    public SessionRepository(AgonDbContext dbContext, ITruthMapRepository truthMapRepo)
    {
        _dbContext = dbContext;
        _truthMapRepo = truthMapRepo;
    }

    public async Task<SessionState> CreateAsync(
        SessionState sessionState,
        CancellationToken cancellationToken = default)
    {
        var entity = new SessionEntity
        {
            Id = sessionState.SessionId,
            UserId = sessionState.UserId,
            Mode = GetSessionMode(sessionState).ToString(),
            FrictionLevel = sessionState.FrictionLevel,
            Status = sessionState.Status.ToString(),
            Phase = sessionState.Phase.ToString(),
            CurrentRound = sessionState.CurrentRound,
            TokensUsed = sessionState.TokensUsed,
            TargetedLoopCount = sessionState.TargetedLoopCount,
            ClarificationIncomplete = sessionState.ClarificationIncomplete,
            ClarificationRoundCount = sessionState.ClarificationRoundCount,
            ForkedFrom = null, // TODO: Add to SessionState when fork support is added
            ForkSnapshotId = null, // TODO: Add to SessionState when fork support is added
            CreatedAt = sessionState.CreatedAt.UtcDateTime,
            UpdatedAt = sessionState.UpdatedAt.UtcDateTime
        };

        _dbContext.Sessions.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return sessionState;
    }

    public async Task<SessionState?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        // Load the Truth Map from the database
        var truthMap = await _truthMapRepo.GetAsync(sessionId, cancellationToken);
        if (truthMap == null)
        {
            // If Truth Map doesn't exist, create an empty one
            // This shouldn't happen in normal operation
            truthMap = TruthMapModel.Empty(sessionId);
        }
        
        return ToSessionState(entity, truthMap);
    }

    public async Task<SessionState?> GetByUserAsync(
        Guid sessionId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        var truthMap = await _truthMapRepo.GetAsync(sessionId, cancellationToken)
            ?? TruthMapModel.Empty(sessionId);

        return ToSessionState(entity, truthMap);
    }

    public async Task UpdateAsync(
        SessionState sessionState,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionState.SessionId, cancellationToken);

        if (entity == null)
        {
            throw new InvalidOperationException(
                $"Cannot update session {sessionState.SessionId}: session not found");
        }

        // Update mutable fields
        entity.Phase = sessionState.Phase.ToString();
        entity.Status = sessionState.Status.ToString();
        entity.CurrentRound = sessionState.CurrentRound;
        entity.TokensUsed = sessionState.TokensUsed;
        entity.TargetedLoopCount = sessionState.TargetedLoopCount;
        entity.ClarificationIncomplete = sessionState.ClarificationIncomplete;
        entity.ClarificationRoundCount = sessionState.ClarificationRoundCount;
        entity.UpdatedAt = DateTime.UtcNow;
        sessionState.UpdatedAt = DateTime.SpecifyKind(entity.UpdatedAt, DateTimeKind.Utc);

        // Explicitly mark entity as modified to ensure EF Core tracks the changes
        _dbContext.Entry(entity).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionState>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.Sessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        var sessionStates = new List<SessionState>();

        foreach (var entity in entities)
        {
            var truthMap = TruthMapModel.Empty(entity.Id);
            sessionStates.Add(ToSessionState(entity, truthMap));
        }

        return sessionStates;
    }

    // ── Helper Methods ───────────────────────────────────────────────────

    private static SessionMode GetSessionMode(SessionState sessionState)
    {
        // Determine mode based on friction level
        // High friction (70+) = Deep mode, otherwise Quick
        return sessionState.FrictionLevel >= 70 ? SessionMode.Deep : SessionMode.Quick;
    }

    private static bool DetermineResearchToolsEnabled(string mode)
    {
        // Research tools typically enabled in Deep mode
        return mode == SessionMode.Deep.ToString();
    }

    private static SessionState ToSessionState(SessionEntity entity, TruthMapModel truthMap)
    {
        var sessionState = SessionState.Create(
            entity.Id,
            entity.UserId,
            idea: string.Empty,
            frictionLevel: entity.FrictionLevel,
            researchToolsEnabled: DetermineResearchToolsEnabled(entity.Mode),
            initialTruthMap: truthMap
        );

        sessionState.Phase = Enum.Parse<SessionPhase>(entity.Phase);
        sessionState.Status = Enum.Parse<SessionStatus>(entity.Status);
        sessionState.CurrentRound = entity.CurrentRound;
        sessionState.TokensUsed = entity.TokensUsed;
        sessionState.TargetedLoopCount = entity.TargetedLoopCount;
        sessionState.ClarificationRoundCount = entity.ClarificationRoundCount;
        sessionState.ClarificationIncomplete = entity.ClarificationIncomplete;
        sessionState.CreatedAt = DateTime.SpecifyKind(entity.CreatedAt, DateTimeKind.Utc);
        sessionState.UpdatedAt = DateTime.SpecifyKind(entity.UpdatedAt, DateTimeKind.Utc);

        return sessionState;
    }
}
