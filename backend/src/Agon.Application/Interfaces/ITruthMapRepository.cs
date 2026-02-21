using Agon.Domain.TruthMap;

namespace Agon.Application.Interfaces;

public interface ITruthMapRepository
{
    Task CreateAsync(TruthMapState map, CancellationToken cancellationToken);
    Task<TruthMapState?> GetAsync(Guid sessionId, CancellationToken cancellationToken);
    Task UpdateAsync(TruthMapState map, CancellationToken cancellationToken);
    Task ApplyPatchAsync(Guid sessionId, TruthMapPatch patch, CancellationToken cancellationToken);
}
