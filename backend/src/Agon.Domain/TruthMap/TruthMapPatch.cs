using System.Text.Json.Serialization;

namespace Agon.Domain.TruthMap;

/// <summary>
/// The ONLY way agents change the Truth Map.
/// Every patch is validated by the Orchestrator before it is applied.
/// </summary>
public sealed record TruthMapPatch(
    [property: JsonPropertyName("ops")] IReadOnlyList<PatchOperation> Ops,
    [property: JsonPropertyName("meta")] PatchMeta Meta);
