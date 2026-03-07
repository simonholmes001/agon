using Agon.Domain.TruthMap.Entities;

namespace Agon.Domain.TruthMap;

/// <summary>Outcome of a patch validation pass.</summary>
public sealed record PatchValidationResult(bool IsValid, string? Reason = null)
{
    public static PatchValidationResult Valid() => new(true);
    public static PatchValidationResult Invalid(string reason) => new(false, reason);
}

/// <summary>
/// Validates a <see cref="TruthMapPatch"/> against the current Truth Map state.
/// The Orchestrator must call this before applying any patch.
///
/// Validation rules (from schemas spec):
/// 1. Non-add ops must reference entity IDs that exist in the Truth Map.
/// 2. Replace/remove ops must target an entity whose ID matches the path target.
/// 3. Agents may not modify the text of claims authored by a different agent.
/// 4. Decision ops must include a non-empty rationale.
/// 5. After Round 2, assumption ops must include a non-empty validation_step.
/// </summary>
public static class PatchValidator
{
    /// <summary>Round number at or after which assumptions require a validation_step.</summary>
    public const int AssumptionValidationRequiredFromRound = 2;

    public static PatchValidationResult Validate(TruthMapPatch patch, TruthMap currentMap)
    {
        foreach (var op in patch.Ops)
        {
            var result = ValidateOperation(op, patch.Meta, currentMap, patch.Meta.Round);
            if (!result.IsValid) return result;
        }

        return PatchValidationResult.Valid();
    }

    // ── Per-operation validation ──────────────────────────────────────────────

    private static PatchValidationResult ValidateOperation(
        PatchOperation op,
        PatchMeta meta,
        TruthMap currentMap,
        int currentRound)
    {
        // Rule 1: Non-add ops must reference existing entity IDs.
        if (op.Op != PatchOp.Add)
        {
            var referencedId = ExtractEntityId(op.Path);
            if (referencedId is not null && !currentMap.EntityExists(referencedId))
                return PatchValidationResult.Invalid(
                    $"Operation '{op.Op}' references entity ID '{referencedId}' which does not exist in the Truth Map.");
        }

        // Rule 2: Replace/remove must target a matching entity ID.
        if (op.Op is PatchOp.Replace or PatchOp.Remove)
        {
            var referencedId = ExtractEntityId(op.Path);
            if (referencedId is not null && !currentMap.EntityExists(referencedId))
                return PatchValidationResult.Invalid(
                    $"Replace/remove op targets path '{op.Path}' but no entity with ID '{referencedId}' exists.");
        }

        // Rule 3: Cross-agent text modification prevention.
        var textModificationError = ValidateTextOwnership(op, meta, currentMap);
        if (textModificationError is not null) return textModificationError;

        // Rule 4 & 5: Content rules for specific entity types.
        if (op.Op == PatchOp.Add && op.Value is not null)
        {
            var contentError = ValidateEntityContent(op, meta, currentMap, currentRound);
            if (contentError is not null) return contentError;
        }

        return PatchValidationResult.Valid();
    }

    // ── Rule 3: Cross-agent text ownership ───────────────────────────────────

    private static PatchValidationResult? ValidateTextOwnership(
        PatchOperation op,
        PatchMeta meta,
        TruthMap currentMap)
    {
        // Only apply to replace/remove on a text field of a claim.
        if (op.Op is not (PatchOp.Replace or PatchOp.Remove)) return null;
        if (!op.Path.Contains("/claims/")) return null;
        if (!op.Path.EndsWith("/text", StringComparison.OrdinalIgnoreCase)) return null;

        var entityId = ExtractEntityId(op.Path);
        if (entityId is null) return null;

        var claim = currentMap.FindClaim(entityId);
        if (claim is null) return null;

        if (claim.ProposedBy != meta.Agent)
            return PatchValidationResult.Invalid(
                $"Agent '{meta.Agent}' attempted to modify the text of claim '{entityId}' authored by '{claim.ProposedBy}'. Cross-agent text modification is not permitted.");

        return null;
    }

    // ── Rules 4 & 5: Entity content requirements ─────────────────────────────

    private static PatchValidationResult? ValidateEntityContent(
        PatchOperation op,
        PatchMeta meta,
        TruthMap currentMap,
        int currentRound)
    {
        _ = currentMap; // not used here but kept for future extension

        // Rule 4: Decision must have a non-empty rationale.
        if (op.Path.Contains("/decisions"))
        {
            var rationale = ExtractStringField(op.Value, "Rationale")
                            ?? ExtractStringField(op.Value, "rationale");
            if (string.IsNullOrWhiteSpace(rationale))
                return PatchValidationResult.Invalid(
                    $"Agent '{meta.Agent}' attempted to add a decision without a 'rationale' field. Decisions require a rationale.");
        }

        // Rule 5: Assumption after Round 2 must have a validation_step.
        if (op.Path.Contains("/assumptions") && currentRound >= AssumptionValidationRequiredFromRound)
        {
            var validationStep = ExtractStringField(op.Value, "ValidationStep")
                                 ?? ExtractStringField(op.Value, "validation_step");
            if (string.IsNullOrWhiteSpace(validationStep))
                return PatchValidationResult.Invalid(
                    $"Agent '{meta.Agent}' attempted to add an assumption without a 'validation_step' field after Round {currentRound}. validation_step is required from Round {AssumptionValidationRequiredFromRound} onward.");
        }

        return null;
    }

    // ── Path and value utilities ──────────────────────────────────────────────

    /// <summary>
    /// Extracts the entity ID from a JSON-Pointer path like "/claims/claim-123/text".
    /// Returns null for append-style paths like "/claims/-".
    /// </summary>
    private static string? ExtractEntityId(string path)
    {
        // e.g. "/claims/claim-123/status" → "claim-123"
        // e.g. "/claims/-" → null (append)
        var segments = path.TrimStart('/').Split('/');
        if (segments.Length < 2) return null;
        var id = segments[1];
        return id == "-" ? null : id;
    }

    /// <summary>
    /// Attempts to read a named property from an anonymous value object.
    /// Supports both strongly-typed records (via reflection) and
    /// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>.
    /// </summary>
    private static string? ExtractStringField(object? value, string fieldName)
    {
        if (value is null) return null;

        // Dictionary<string,object>
        if (value is System.Collections.Generic.Dictionary<string, object> dict)
        {
            return dict.TryGetValue(fieldName, out var v) ? v?.ToString() : null;
        }

        // Record / class — use reflection
        var prop = value.GetType().GetProperty(fieldName,
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.IgnoreCase);

        return prop?.GetValue(value)?.ToString();
    }
}
