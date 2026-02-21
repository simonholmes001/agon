using Agon.Domain.TruthMap;

namespace Agon.Domain.Snapshots;

/// <summary>
/// Request to fork a session from a prior snapshot (Pause-and-Replay).
/// </summary>
public class ForkRequest
{
    public required Guid ParentSessionId { get; init; }
    public required Guid SnapshotId { get; init; }
    public required string Label { get; init; }
    public List<TruthMapPatch> InitialPatches { get; init; } = new();
}
