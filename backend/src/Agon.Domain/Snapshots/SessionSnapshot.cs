using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Domain.Snapshots;

/// <summary>
/// Immutable point-in-time capture of the Truth Map at the end of a round.
/// Once created, a snapshot is never modified — it is the source of truth
/// for the Pause-and-Replay (Fork) feature.
/// </summary>
public sealed record SessionSnapshot
{
    public Guid SnapshotId { get; init; }
    public Guid SessionId { get; init; }
    public int Round { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>SHA-256 of the serialised TruthMap for integrity verification.</summary>
    public string TruthMapHash { get; init; } = string.Empty;

    /// <summary>Full Truth Map state at the moment this snapshot was taken.</summary>
    public TruthMapModel TruthMap { get; init; } = default!;

    // ── Factory ──────────────────────────────────────────────────────────────

    public static SessionSnapshot Create(TruthMapModel truthMap, int round)
    {
        ArgumentNullException.ThrowIfNull(truthMap);

        var json = JsonSerializer.Serialize(truthMap);
        var hash = ComputeSha256(json);

        return new SessionSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            SessionId = truthMap.SessionId,
            Round = round,
            CreatedAt = DateTimeOffset.UtcNow,
            TruthMapHash = hash,
            TruthMap = truthMap
        };
    }

    /// <summary>
    /// Verifies that the stored hash still matches the Truth Map payload.
    /// Returns false if the snapshot has been tampered with.
    /// </summary>
    public bool IsIntact()
    {
        var json = JsonSerializer.Serialize(TruthMap);
        return ComputeSha256(json) == TruthMapHash;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
