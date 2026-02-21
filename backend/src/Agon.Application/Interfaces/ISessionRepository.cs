using Agon.Application.Sessions;

namespace Agon.Application.Interfaces;

public interface ISessionRepository
{
    Task CreateAsync(SessionState session, CancellationToken cancellationToken);
    Task<SessionState?> GetAsync(Guid sessionId, CancellationToken cancellationToken);
    Task UpdateAsync(SessionState session, CancellationToken cancellationToken);
}
