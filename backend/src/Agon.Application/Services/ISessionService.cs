using Agon.Application.Models;
using Agon.Domain.Sessions;
using Agon.Domain.Snapshots;

namespace Agon.Application.Services;

/// <summary>
/// Abstraction over <see cref="SessionService"/> to allow test isolation in the Orchestrator.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Creates a new session with user context and initial idea.
    /// </summary>
    Task<SessionState> CreateAsync(
        Guid userId,
        string idea,
        int frictionLevel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new session with explicit research tools flag (legacy method).
    /// </summary>
    Task<SessionState> CreateAsync(
        int frictionLevel,
        bool researchToolsEnabled,
        CancellationToken cancellationToken = default);

    Task<SessionState?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the session only if it belongs to the specified user.
    /// Returns null when the session does not exist or is not owned by <paramref name="userId"/>.
    /// Prefer this method for all user-facing reads as it enforces ownership at the service layer.
    /// </summary>
    Task<SessionState?> GetByUserAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionState>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionAttachment>> ListAttachmentsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionAttachment>> ListPendingAttachmentExtractionsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<SessionAttachment> SaveAttachmentAsync(
        SessionAttachment attachment,
        CancellationToken cancellationToken = default);

    Task<SessionAttachment> UpdateAttachmentExtractionStateAsync(
        Guid attachmentId,
        AttachmentExtractionStatus extractionStatus,
        string? extractedText,
        string? extractionFailureReason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins the clarification phase for a session.
    /// </summary>
    Task StartClarificationAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a user message during the clarification phase and triggers moderator evaluation.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="content">The user's message content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    /// <exception cref="InvalidOperationException">If session not found or not in clarification phase</exception>
    Task SubmitMessageAsync(Guid sessionId, string content, CancellationToken cancellationToken = default);

    Task AdvancePhaseAsync(
        SessionState state,
        SessionPhase nextPhase,
        CancellationToken cancellationToken = default);

    Task RecordRoundSnapshotAsync(
        SessionState state,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionSnapshot>> ListSnapshotsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
