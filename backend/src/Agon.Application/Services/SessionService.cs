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
    private readonly IAttachmentRepository? _attachmentRepo;
    private readonly IEventBroadcaster? _broadcaster;
    private readonly Lazy<IOrchestrator>? _lazyOrchestrator;

    public SessionService(
        ISessionRepository sessionRepo,
        ITruthMapRepository truthMapRepo,
        ISnapshotStore snapshotStore,
        IAttachmentRepository? attachmentRepo = null,
        IEventBroadcaster? broadcaster = null,
        Lazy<IOrchestrator>? orchestrator = null)
    {
        _sessionRepo = sessionRepo;
        _truthMapRepo = truthMapRepo;
        _snapshotStore = snapshotStore;
        _attachmentRepo = attachmentRepo;
        _broadcaster = broadcaster;
        _lazyOrchestrator = orchestrator;
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

        // Seed the user's initial idea into the CoreIdea field
        if (!string.IsNullOrWhiteSpace(idea))
        {
            truthMap = truthMap with { CoreIdea = idea };
        }

        // Persist Truth Map with seeded CoreIdea
        await _truthMapRepo.SaveAsync(truthMap, cancellationToken);

        // Create session state with user context
        var state = SessionState.Create(sessionId, userId, idea, frictionLevel, researchToolsEnabled: false, truthMap);

        // Persist session record
        return await _sessionRepo.CreateAsync(state, cancellationToken);
    }

    public async Task<SessionState?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var state = await _sessionRepo.GetAsync(sessionId, cancellationToken);
        if (state is null)
        {
            return null;
        }

        await HydrateAttachmentsAsync(state, cancellationToken);
        return state;
    }

    public async Task<SessionState?> GetByUserAsync(
        Guid sessionId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var state = await _sessionRepo.GetByUserAsync(sessionId, userId, cancellationToken);
        if (state is null)
        {
            return null;
        }

        await HydrateAttachmentsAsync(state, cancellationToken);
        return state;
    }

    public async Task<IReadOnlyList<SessionState>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _sessionRepo.ListByUserAsync(userId, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionAttachment>> ListAttachmentsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_attachmentRepo is null)
        {
            return Array.Empty<SessionAttachment>();
        }

        return await _attachmentRepo.ListBySessionAsync(sessionId, cancellationToken);
    }

    public async Task<SessionAttachment> SaveAttachmentAsync(
        SessionAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        if (_attachmentRepo is null)
        {
            throw new InvalidOperationException("Attachment repository is not configured.");
        }

        return await _attachmentRepo.CreateAsync(attachment, cancellationToken);
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

        await HydrateAttachmentsAsync(state, cancellationToken);

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
        if (_lazyOrchestrator?.Value is { } orchestrator)
        {
            await orchestrator.RunModeratorAsync(state, cancellationToken);
        }
    }

    public async Task SubmitMessageAsync(
        Guid sessionId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var state = await _sessionRepo.GetAsync(sessionId, cancellationToken);
        
        if (state is null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        await HydrateAttachmentsAsync(state, cancellationToken);

        var isClarificationFlow = state.Phase == SessionPhase.Clarification;
        var isPostDeliveryFlow = state.Phase is SessionPhase.Deliver
            or SessionPhase.DeliverWithGaps
            or SessionPhase.PostDelivery;

        if (!isClarificationFlow && !isPostDeliveryFlow)
        {
            throw new InvalidOperationException(
                $"Cannot submit message in phase {state.Phase}. Session must be in Clarification, Deliver, DeliverWithGaps, or PostDelivery phase.");
        }

        // Add the user message to the session state
        var userMessage = new UserMessage(
            content,
            DateTimeOffset.UtcNow,
            isClarificationFlow ? state.ClarificationRoundCount : state.CurrentRound);
        
        state.UserMessages.Add(userMessage);

        if (_lazyOrchestrator?.Value is { } orchestrator)
        {
            if (isClarificationFlow)
            {
                // Clarification flow: moderator asks follow-up questions or signals READY.
                await orchestrator.RunModeratorAsync(state, cancellationToken);
            }
            else
            {
                // Post-delivery flow: single assistant handles follow-up Q&A and revisions.
                await orchestrator.RunPostDeliveryFollowUpAsync(state, content, cancellationToken);
            }
        }

        // Persist the updated state AFTER orchestrator (to capture any state changes)
        await _sessionRepo.UpdateAsync(state, cancellationToken);
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

    private async Task HydrateAttachmentsAsync(SessionState state, CancellationToken cancellationToken)
    {
        if (_attachmentRepo is null)
        {
            return;
        }

        var attachments = await _attachmentRepo.ListBySessionAsync(state.SessionId, cancellationToken);
        state.Attachments.Clear();
        state.Attachments.AddRange(attachments);
    }
}
