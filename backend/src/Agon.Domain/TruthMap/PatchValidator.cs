using Agon.Domain.TruthMap.Entities;

namespace Agon.Domain.TruthMap;

/// <summary>
/// Validates TruthMapPatch operations against the current TruthMapState.
/// Implements the 5 validation rules from the schemas specification.
/// </summary>
public static class PatchValidator
{
    /// <summary>
    /// Validates a patch against the current Truth Map state.
    /// Returns a ValidationResult with all errors found.
    /// </summary>
    public static ValidationResult Validate(TruthMapPatch patch, TruthMapState map)
    {
        var errors = new List<string>();

        foreach (var op in patch.Ops)
        {
            ValidateEntityReference(op, map, errors);
            ValidateEntityIdMatch(op, errors);
            ValidateCrossAgentTextModification(op, patch.Meta.Agent, map, errors);
            ValidateDecisionRationale(op, errors);
            ValidateAssumptionValidationStep(op, map, errors);
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }

    /// <summary>
    /// Rule 1: Reject patches referencing non-existent entity IDs (unless op is add).
    /// </summary>
    private static void ValidateEntityReference(PatchOperation op, TruthMapState map, List<string> errors)
    {
        if (op.Op == PatchOperationType.Add)
            return;

        var entityId = ExtractEntityId(op.Path);
        if (entityId is null)
            return;

        if (!map.EntityExists(entityId))
        {
            errors.Add($"Entity '{entityId}' does not exist in the Truth Map. Cannot {op.Op.ToString().ToLowerInvariant()} a non-existent entity.");
        }
    }

    /// <summary>
    /// Rule 2: Reject replace/remove on an entity whose id does not match the target.
    /// </summary>
    private static void ValidateEntityIdMatch(PatchOperation op, List<string> errors)
    {
        if (op.Op != PatchOperationType.Replace && op.Op != PatchOperationType.Remove)
            return;

        var pathEntityId = ExtractEntityId(op.Path);
        if (pathEntityId is null)
            return;

        // Only check when the value is a full entity object with an Id property
        var valueId = ExtractIdFromValue(op.Value);
        if (valueId is null)
            return;

        if (pathEntityId != valueId)
        {
            errors.Add($"Entity ID mismatch: path targets '{pathEntityId}' but value has id '{valueId}'.");
        }
    }

    /// <summary>
    /// Rule 3: Prevent cross-agent text modification.
    /// Agents can update their own claims' text; they can add challenged_by references
    /// to others' claims but must not overwrite others' text.
    /// </summary>
    private static void ValidateCrossAgentTextModification(PatchOperation op, string patchAgent, TruthMapState map, List<string> errors)
    {
        if (op.Op != PatchOperationType.Replace)
            return;

        // Only applies to claim text modifications: /claims/{id}/text
        if (!op.Path.EndsWith("/text", StringComparison.Ordinal))
            return;

        var entityId = ExtractEntityId(op.Path);
        if (entityId is null)
            return;

        var claim = map.FindClaim(entityId);
        if (claim is null)
            return;

        if (!string.Equals(claim.Agent, patchAgent, StringComparison.Ordinal))
        {
            errors.Add($"Cross-agent text modification rejected: agent '{patchAgent}' cannot modify text of claim '{entityId}' authored by '{claim.Agent}'.");
        }
    }

    /// <summary>
    /// Rule 4: Require rationale on decisions.
    /// </summary>
    private static void ValidateDecisionRationale(PatchOperation op, List<string> errors)
    {
        if (op.Value is not Decision decision)
            return;

        if (string.IsNullOrWhiteSpace(decision.Rationale))
        {
            errors.Add($"Decision '{decision.Id}' requires a non-empty rationale.");
        }
    }

    /// <summary>
    /// Rule 5: Require validation_step on assumptions after Round 2.
    /// Null is not acceptable after Round 2.
    /// </summary>
    private static void ValidateAssumptionValidationStep(PatchOperation op, TruthMapState map, List<string> errors)
    {
        if (op.Value is not Assumption assumption)
            return;

        if (map.Round > 2 && string.IsNullOrWhiteSpace(assumption.ValidationStep))
        {
            errors.Add($"Assumption '{assumption.Id}' requires a non-empty validation_step after Round 2.");
        }
    }

    /// <summary>
    /// Extracts the entity ID from a JSON Patch-style path.
    /// Examples:
    ///   /claims/c1/status → c1
    ///   /claims/c1 → c1
    ///   /claims/c1/challenged_by/- → c1
    ///   /risks/- → null (append, no specific entity)
    ///   /decisions/- → null
    /// </summary>
    private static string? ExtractEntityId(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Need at least collection/entityId
        if (segments.Length < 2)
            return null;

        var candidate = segments[1];

        // "-" is the JSON Patch append indicator, not an entity ID
        if (candidate == "-")
            return null;

        return candidate;
    }

    /// <summary>
    /// Attempts to extract an Id property from the patch value.
    /// Returns null if the value is not an entity with an Id.
    /// </summary>
    private static string? ExtractIdFromValue(object? value)
    {
        return value switch
        {
            Claim c => c.Id,
            Assumption a => a.Id,
            Decision d => d.Id,
            Risk r => r.Id,
            Evidence e => e.Id,
            OpenQuestion q => q.Id,
            Persona p => p.Id,
            _ => null
        };
    }
}
