using Agon.Domain.TruthMap;

namespace Agon.Domain.Snapshots;

/// <summary>
/// A request to initiate a Pause-and-Replay branch from a historical snapshot.
/// The forked session starts from the snapshot's Truth Map state, with
/// <see cref="InitialPatches"/> applied before the debate resumes.
/// </summary>
public sealed record ForkRequest(
    Guid ParentSessionId,
    Guid SnapshotId,
    IReadOnlyList<TruthMapPatch> InitialPatches,
    string Label)
{
    /// <summary>
    /// Validates the fork request.
    /// Returns an error message if invalid, null if valid.
    /// </summary>
    public string? Validate()
    {
        if (ParentSessionId == Guid.Empty)
            return "ParentSessionId must not be empty.";

        if (SnapshotId == Guid.Empty)
            return "SnapshotId must not be empty.";

        if (string.IsNullOrWhiteSpace(Label))
            return "Label must not be empty — it describes the scenario being explored.";

        return null;
    }
}
