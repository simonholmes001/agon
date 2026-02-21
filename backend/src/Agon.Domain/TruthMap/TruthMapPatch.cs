namespace Agon.Domain.TruthMap;

public enum PatchOperationType
{
    Add,
    Replace,
    Remove
}

/// <summary>
/// A single operation within a TruthMapPatch.
/// </summary>
public class PatchOperation
{
    public PatchOperationType Op { get; init; }
    public required string Path { get; init; }
    public object? Value { get; init; }
}

/// <summary>
/// Metadata for a TruthMapPatch — who proposed it, when, and why.
/// </summary>
public class PatchMeta
{
    public required string Agent { get; init; }
    public int Round { get; init; }
    public string Reason { get; set; } = string.Empty;
    public Guid SessionId { get; init; }
}

/// <summary>
/// The only way agents change the Truth Map.
/// Every patch is validated by the Orchestrator before being applied.
/// </summary>
public class TruthMapPatch
{
    public List<PatchOperation> Ops { get; init; } = new();
    public required PatchMeta Meta { get; init; }
}
