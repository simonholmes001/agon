using System.Text.Json.Serialization;

namespace Agon.Domain.TruthMap;

/// <summary>
/// Provenance metadata attached to every Truth Map patch operation.
/// Provides full audit trail: which agent changed what, in which round, and why.
/// </summary>
public sealed record PatchMeta(
    [property: JsonPropertyName("agent")] string Agent,
    [property: JsonPropertyName("round")] int Round,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("session_id")] Guid SessionId);
