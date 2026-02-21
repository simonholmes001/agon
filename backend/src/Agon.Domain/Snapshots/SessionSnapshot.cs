using System.Security.Cryptography;
using System.Text;
using Agon.Domain.TruthMap;

namespace Agon.Domain.Snapshots;

/// <summary>
/// Immutable snapshot written at the end of every round.
/// Used for Pause-and-Replay and session history.
/// </summary>
public class SessionSnapshot
{
    public Guid SnapshotId { get; init; }
    public Guid SessionId { get; init; }
    public int Round { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string TruthMapHash { get; init; } = string.Empty;
    public TruthMapState TruthMap { get; init; } = null!;

    private SessionSnapshot() { }

    /// <summary>
    /// Creates a new immutable snapshot from the current Truth Map state.
    /// Computes a SHA-256 hash of the Truth Map for integrity verification.
    /// </summary>
    public static SessionSnapshot Create(Guid sessionId, int round, TruthMapState truthMap)
    {
        return new SessionSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            SessionId = sessionId,
            Round = round,
            CreatedAt = DateTimeOffset.UtcNow,
            TruthMapHash = ComputeHash(truthMap),
            TruthMap = truthMap
        };
    }

    private static string ComputeHash(TruthMapState map)
    {
        // Deterministic string representation for hashing.
        // In production this would use proper JSON serialization;
        // for the Domain layer (zero dependencies) we use a simple
        // composite of the key state.
        var sb = new StringBuilder();
        sb.Append(map.SessionId);
        sb.Append(map.Version);
        sb.Append(map.Round);
        sb.Append(map.CoreIdea);

        foreach (var claim in map.Claims)
        {
            sb.Append(claim.Id);
            sb.Append(claim.Text);
            sb.Append(claim.Confidence);
            sb.Append(claim.Status);
        }

        foreach (var assumption in map.Assumptions)
        {
            sb.Append(assumption.Id);
            sb.Append(assumption.Text);
            sb.Append(assumption.Status);
        }

        foreach (var decision in map.Decisions)
        {
            sb.Append(decision.Id);
            sb.Append(decision.Text);
        }

        foreach (var risk in map.Risks)
        {
            sb.Append(risk.Id);
            sb.Append(risk.Text);
            sb.Append(risk.Severity);
        }

        foreach (var evidence in map.Evidence)
        {
            sb.Append(evidence.Id);
            sb.Append(evidence.Title);
        }

        foreach (var question in map.OpenQuestions)
        {
            sb.Append(question.Id);
            sb.Append(question.Text);
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
