using Agon.Application.Interfaces;
using Agon.Domain.Snapshots;
using StackExchange.Redis;
using System.Text.Json;

namespace Agon.Infrastructure.Persistence.Redis;

/// <summary>
/// Redis implementation of ISnapshotStore.
/// Stores immutable snapshots with TTL and maintains session→snapshots index.
/// </summary>
public sealed class RedisSnapshotStore : ISnapshotStore
{
    private readonly IDatabase _database;
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromDays(30); // 30 day retention

    // Use default serialization options to match System.Text.Json defaults
    // (PascalCase property names)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public RedisSnapshotStore(IDatabase database)
    {
        _database = database;
    }

    public async Task<SessionSnapshot> SaveAsync(
        SessionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var key = GetSnapshotKey(snapshot.SnapshotId);
        var sessionSetKey = GetSessionSnapshotsKey(snapshot.SessionId);

        // Serialize snapshot
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);

        // Store snapshot with TTL
        await _database.StringSetAsync(key, json, SnapshotTtl);

        // Add snapshot ID to session's snapshot set
        var snapshotReference = $"snapshot:{snapshot.SnapshotId}";
        await _database.SetAddAsync(sessionSetKey, snapshotReference);

        // Set TTL on the session's snapshot set as well
        await _database.KeyExpireAsync(sessionSetKey, SnapshotTtl);

        return snapshot;
    }

    public async Task<SessionSnapshot?> GetAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        var key = GetSnapshotKey(snapshotId);
        var json = await _database.StringGetAsync(key);

        if (json.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<SessionSnapshot>(json.ToString(), JsonOptions);
            return snapshot;
        }
        catch (JsonException)
        {
            // If deserialization fails, return null
            return null;
        }
    }

    public async Task<IReadOnlyList<SessionSnapshot>> ListBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionSetKey = GetSessionSnapshotsKey(sessionId);
        
        // Get all snapshot references for this session
        var snapshotReferences = await _database.SetMembersAsync(sessionSetKey);

        if (snapshotReferences.Length == 0)
        {
            return Array.Empty<SessionSnapshot>();
        }

        // Extract snapshot IDs from references
        var snapshots = new List<SessionSnapshot>();

        foreach (var reference in snapshotReferences)
        {
            var snapshotIdStr = reference.ToString().Replace("snapshot:", string.Empty);
            if (Guid.TryParse(snapshotIdStr, out var snapshotId))
            {
                var snapshot = await GetAsync(snapshotId, cancellationToken);
                if (snapshot != null)
                {
                    snapshots.Add(snapshot);
                }
            }
        }

        // Order by round ascending
        return snapshots
            .OrderBy(s => s.Round)
            .ToList();
    }

    // ── Key Generation ───────────────────────────────────────────────────

    private static RedisKey GetSnapshotKey(Guid snapshotId) =>
        $"snapshot:{snapshotId}";

    private static RedisKey GetSessionSnapshotsKey(Guid sessionId) =>
        $"session:{sessionId}:snapshots";
}
