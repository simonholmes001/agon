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
            UserId = Guid.Empty, // TODO: Get from auth context when auth is implemented
            Mode = GetSessionMode(sessionState).ToString(),
            FrictionLevel = sessionState.FrictionLevel,
            Status = sessionState.Status.ToString(),
            Phase = sessionState.Phase.ToString(),
            ForkedFrom = null, // TODO: Add to SessionState when fork support is added
            ForkSnapshotId = null, // TODO: Add to SessionState when fork support is added
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
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
        
        var sessionState = SessionState.Create(
            sessionId,
            frictionLevel: entity.FrictionLevel,
            researchToolsEnabled: DetermineResearchToolsEnabled(entity.Mode),
            initialTruthMap: truthMap
        );

        // Update mutable properties
        sessionState.Phase = Enum.Parse<SessionPhase>(entity.Phase);
        sessionState.Status = Enum.Parse<SessionStatus>(entity.Status);
        sessionState.CurrentRound = 0; // TODO: Store in entity if needed
        sessionState.TokensUsed = 0; // TODO: Store in entity if needed
        sessionState.TargetedLoopCount = 0; // TODO: Store in entity if needed
        sessionState.ClarificationRoundCount = 0; // TODO: Store in entity if needed
        sessionState.ClarificationIncomplete = false; // TODO: Store in entity if needed

        return sessionState;
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
        entity.UpdatedAt = DateTime.UtcNow;

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
            
            var sessionState = SessionState.Create(
                entity.Id,
                frictionLevel: entity.FrictionLevel,
                researchToolsEnabled: DetermineResearchToolsEnabled(entity.Mode),
                initialTruthMap: truthMap
            );

            sessionState.Phase = Enum.Parse<SessionPhase>(entity.Phase);
            sessionState.Status = Enum.Parse<SessionStatus>(entity.Status);

            sessionStates.Add(sessionState);
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
}
