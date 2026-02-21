using System.Collections.Concurrent;
using Agon.Application.Interfaces;
using Agon.Domain.TruthMap;

namespace Agon.Infrastructure.Persistence.InMemory;

public class InMemoryTruthMapRepository : ITruthMapRepository
{
    private readonly ConcurrentDictionary<Guid, TruthMapState> maps = new();

    public Task CreateAsync(TruthMapState map, CancellationToken cancellationToken)
    {
        maps[map.SessionId] = map.DeepCopy();
        return Task.CompletedTask;
    }

    public Task<TruthMapState?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        maps.TryGetValue(sessionId, out var map);
        return Task.FromResult(map is null ? null : map.DeepCopy());
    }

    public Task UpdateAsync(TruthMapState map, CancellationToken cancellationToken)
    {
        maps[map.SessionId] = map.DeepCopy();
        return Task.CompletedTask;
    }

    public Task ApplyPatchAsync(Guid sessionId, TruthMapPatch patch, CancellationToken cancellationToken)
    {
        if (!maps.TryGetValue(sessionId, out var map))
        {
            throw new KeyNotFoundException($"Truth map for session '{sessionId}' was not found.");
        }

        // Vertical-slice behavior: keep state deterministic and auditable
        // while full patch-application logic is added in later persistence work.
        map.Round = Math.Max(map.Round, patch.Meta.Round);
        map.IncrementVersion();
        maps[sessionId] = map;
        return Task.CompletedTask;
    }
}
