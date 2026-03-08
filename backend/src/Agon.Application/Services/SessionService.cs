using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Application.Orchestration;
using Agon.Domain.Sessions;
using Agon.Domain.Snapshots;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Application.Services;

/// <summary>
/// Application service for session lifecycle operations.
/// Orchestrates reads/writes across ISessionRepository, ITruthMapRepository, and ISnapshotStore.
/// </summary>
public sealed class SessionService : ISessionService
{
    private readonly ISessionRepository _sessionRepo;
    private readonly ITruthMapRepository _truthMapRepo;
    private readonly ISnapshotStore _snapshotStore;
    private readonly IEventBroadcaster? _broadcaster;
    private readonly IOrchestrator? _orchestrator;

    public SessionService(
        ISessionRepository sessionRepo,
        ITruthMapRepository truthMapRepo,
        ISnapshotStore snapshotStore,
        IEventBroadcaster? broadcaster = null,
        IOrchestrator? orchestrator = null)
    {
        _sessionRepo = sessionRepo;
        _truthMapRepo = truthMapRepo;
        _snapshotStore = snapshotStore;
        _broadcaster = broadcaster;
        _orchestrator = orchestrator;
    }

    public async Task<SessionState> CreateAsync(
        int frictionLevel,
        bool researchToolsEnabled,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);

        // Persist empty Truth Map
        await _truthMapRepo.SaveAsync(truthMap, cancellationToken);

        // Create session state (legacy method uses empty userId/idea)
        var state = SessionState.Create(sessionId, frictionLevel, researchToolsEnabled, truthMap);

        // Persist session record
        return await _sessionRepo.CreateAsync(state, cancellationToken);
    }

    public async Task<SessionState> CreateAsync(
        Guid userId,
        string idea,
        int frictionLevel,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();
        var truthMap = TruthMapModel.Empty(sessionId);

        // Persist empty Truth Map
        await _truthMapRepo.SaveAsync(truthMap, cancellationToken);

        // Create session state with user context
        var state = SessionState.Create(sessionId, userId, idea, frictionLevel, researchToolsEnabled: false, truthMap);

        // Persist session record
        return await _sessionRepo.CreateAsync(state, cancellationToken);
    }

    public async Task<SessionState?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return await _sessionRepo.GetAsync(sessionId, cancellationToken);
    }

    public async Task StartClarificationAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var state = await _sessionRepo.GetAsync(sessionId, cancellationToken);
        
        if (state is null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (state.Phase != SessionPhase.Intake)
        {
            throw new InvalidOperationException(
                $"Cannot start clarification from phase {state.Phase}. Session must be in Intake phase.");
        }

        // Transition to Clarification phase
        state.Phase = SessionPhase.Clarification;
        await _sessionRepo.UpdateAsync(state, cancellationToken);

        // Broadcast phase transition event
        if (_broadcaster is not null)
        {
            await _broadcaster.SendRoundProgressAsync(
                state.SessionId,
                SessionPhase.Clarification.ToString(),
                state.Status.ToString(),
                cancellationToken);
        }

        // ⚡ NEW: Call Orchestrator to run Moderator agent
        if (_orchestrator is not null)
        {
            await _orchestrator.RunModeratorAsync(state, cancellationToken);
        }
    }

    public async Task AdvancePhaseAsync(
        SessionState state,
        SessionPhase nextPhase,
        CancellationToken cancellationToken = default)
    {
        state.Phase = nextPhase;
        await _sessionRepo.UpdateAsync(state, cancellationToken);

        if (_broadcaster is not null)
        {
            await _broadcaster.SendRoundProgressAsync(
                state.SessionId,
                nextPhase.ToString(),
                state.Status.ToString(),
                cancellationToken);
        }
    }

    public async Task RecordRoundSnapshotAsync(
        SessionState state,
        CancellationToken cancellationToken = default)
    {
        var snapshot = SessionSnapshot.Create(state.TruthMap, state.CurrentRound);
        await _snapshotStore.SaveAsync(snapshot, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionSnapshot>> ListSnapshotsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return await _snapshotStore.ListBySessionAsync(sessionId, cancellationToken);
    }
}
