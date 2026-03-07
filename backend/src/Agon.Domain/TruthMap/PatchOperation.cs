using System.Text.Json.Serialization;

namespace Agon.Domain.TruthMap;

/// <summary>
/// Supported patch operation types, following JSON Patch conventions (RFC 6902).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PatchOp>))]
public enum PatchOp
{
    Add,
    Replace,
    Remove
}

/// <summary>
/// A single atomic change to the Truth Map.
/// </summary>
/// <param name="Op">The operation type.</param>
/// <param name="Path">
/// JSON-Pointer-style path, e.g. "/claims/-" (append), "/claims/0/status" (replace field).
/// </param>
/// <param name="Value">
/// For <see cref="PatchOp.Add"/> and <see cref="PatchOp.Replace"/> — the new value as a raw object.
/// Must be null for <see cref="PatchOp.Remove"/>.
/// </param>
public sealed record PatchOperation(
    [property: JsonPropertyName("op")] PatchOp Op,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("value")] object? Value);
