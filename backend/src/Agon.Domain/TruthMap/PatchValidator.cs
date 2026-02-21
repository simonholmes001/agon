using Agon.Domain.TruthMap.Entities;

namespace Agon.Domain.TruthMap;

/// <summary>
/// Validates TruthMapPatch operations against the current TruthMapState.
/// Implements the 5 validation rules from the schemas specification.
/// </summary>
public static class PatchValidator
{
    private static readonly HashSet<string> EntityCollections = new(StringComparer.Ordinal)
    {
        "claims",
        "assumptions",
        "decisions",
        "risks",
        "evidence",
        "open_questions",
        "personas"
    };

    private readonly record struct EntityTarget(string? EntityId, string? ErrorMessage);

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
            ValidateEntityIdMatch(op, map, errors);
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

        if (!TryResolveTargetEntity(op.Path, map, out var target))
            return;

        if (target.EntityId is null)
        {
            errors.Add(target.ErrorMessage ?? $"Path '{op.Path}' does not target an existing entity.");
            return;
        }

        if (!map.EntityExists(target.EntityId))
        {
            errors.Add($"Entity '{target.EntityId}' does not exist in the Truth Map. Cannot {op.Op.ToString().ToLowerInvariant()} a non-existent entity.");
        }
    }

    /// <summary>
    /// Rule 2: Reject replace/remove on an entity whose id does not match the target.
    /// </summary>
    private static void ValidateEntityIdMatch(PatchOperation op, TruthMapState map, List<string> errors)
    {
        if (op.Op != PatchOperationType.Replace && op.Op != PatchOperationType.Remove)
            return;

        if (!TryResolveTargetEntity(op.Path, map, out var target))
            return;

        if (target.EntityId is null)
            return;

        // Only check when the value is a full entity object with an Id property
        var valueId = ExtractIdFromValue(op.Value);
        if (valueId is null)
            return;

        if (target.EntityId != valueId)
        {
            errors.Add($"Entity ID mismatch: path targets '{target.EntityId}' but value has id '{valueId}'.");
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

        // Only applies to claim text modifications: /claims/{index-or-id}/text
        if (!IsClaimTextPath(op.Path))
            return;

        if (!TryResolveTargetEntity(op.Path, map, out var target) || target.EntityId is null)
            return;

        var claim = map.FindClaim(target.EntityId);
        if (claim is null)
            return;

        if (!string.Equals(claim.Agent, patchAgent, StringComparison.Ordinal))
        {
            errors.Add($"Cross-agent text modification rejected: agent '{patchAgent}' cannot modify text of claim '{target.EntityId}' authored by '{claim.Agent}'.");
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
    /// Parses paths and resolves either ID-based (/claims/c1/status)
    /// or index-based (/claims/0/status) entity selectors.
    /// Returns false for non-entity paths (e.g. /constraints/budget).
    /// </summary>
    private static bool TryResolveTargetEntity(string path, TruthMapState map, out EntityTarget target)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            target = new EntityTarget(null, $"Path '{path}' is invalid.");
            return true;
        }

        var collection = segments[0];
        if (!EntityCollections.Contains(collection))
        {
            target = default;
            return false;
        }

        if (segments.Length < 2)
        {
            target = new EntityTarget(null, $"Path '{path}' is missing an entity selector.");
            return true;
        }

        var selector = segments[1];
        if (selector == "-")
        {
            target = new EntityTarget(null, $"Path '{path}' uses '-' append selector, which does not target an existing entity.");
            return true;
        }

        if (int.TryParse(selector, out var index))
        {
            if (index < 0)
            {
                target = new EntityTarget(null, $"Path '{path}' contains a negative index '{selector}'.");
                return true;
            }

            var entityId = GetEntityIdByIndex(collection, index, map);
            target = entityId is null
                ? new EntityTarget(null, $"Path '{path}' references index {index} in '{collection}', but no entity exists at that position.")
                : new EntityTarget(entityId, null);
            return true;
        }

        target = new EntityTarget(selector, null);
        return true;
    }

    private static bool IsClaimTextPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 3
            && segments[0] == "claims"
            && segments[2] == "text";
    }

    private static string? GetEntityIdByIndex(string collection, int index, TruthMapState map)
    {
        return collection switch
        {
            "claims" => GetIdByIndex(map.Claims, index, claim => claim.Id),
            "assumptions" => GetIdByIndex(map.Assumptions, index, assumption => assumption.Id),
            "decisions" => GetIdByIndex(map.Decisions, index, decision => decision.Id),
            "risks" => GetIdByIndex(map.Risks, index, risk => risk.Id),
            "evidence" => GetIdByIndex(map.Evidence, index, evidence => evidence.Id),
            "open_questions" => GetIdByIndex(map.OpenQuestions, index, question => question.Id),
            "personas" => GetIdByIndex(map.Personas, index, persona => persona.Id),
            _ => null
        };
    }

    private static string? GetIdByIndex<T>(IReadOnlyList<T> entities, int index, Func<T, string> getId)
    {
        if (index < 0 || index >= entities.Count)
            return null;

        return getId(entities[index]);
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
