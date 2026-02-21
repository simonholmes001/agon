namespace Agon.Domain.Sessions;

/// <summary>
/// Lifecycle status of a session.
/// </summary>
public enum SessionStatus
{
    Active,
    Paused,
    Complete,
    CompleteWithGaps,
    Forked,
    Closed
}
