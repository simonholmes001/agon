using Agon.Application.Interfaces;
using Agon.Domain.Engines;
using Agon.Domain.TruthMap;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Infrastructure.Persistence.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of ITruthMapRepository using EF Core.
/// Stores current Truth Map state as JSONB and maintains append-only patch event log.
/// </summary>
public class TruthMapRepository : ITruthMapRepository
{
    private readonly AgonDbContext _dbContext;
    private readonly JsonSerializerOptions _jsonOptions;

    public TruthMapRepository(AgonDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task<TruthMapModel?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.TruthMaps
            .AsNoTracking()
            .FirstOrDefaultAsync(tm => tm.SessionId == sessionId, cancellationToken);

        if (entity == null)
        {
            // Return new Truth Map for new sessions
            return TruthMapModel.Empty(sessionId);
        }

        try
        {
            var truthMap = JsonSerializer.Deserialize<TruthMapModel>(entity.CurrentState, _jsonOptions);
            return truthMap ?? TruthMapModel.Empty(sessionId);
        }
        catch (JsonException)
        {
            // If deserialization fails, return new Truth Map
            return TruthMapModel.Empty(sessionId);
        }
    }

    public async Task<TruthMapModel> ApplyPatchAsync(
        Guid sessionId,
        TruthMapPatch patch,
        CancellationToken cancellationToken = default)
    {
        // Get current Truth Map
        var currentTruthMap = await GetAsync(sessionId, cancellationToken) ?? TruthMapModel.Empty(sessionId);

        // Apply patch operations (in-memory for now - proper patch application would use the PatchValidator)
        var updatedTruthMap = ApplyPatchOperations(currentTruthMap, patch);

        // Serialize updated Truth Map
        var serialized = JsonSerializer.Serialize(updatedTruthMap, _jsonOptions);

        // Upsert Truth Map entity
        var entity = await _dbContext.TruthMaps
            .FirstOrDefaultAsync(tm => tm.SessionId == sessionId, cancellationToken);

        if (entity == null)
        {
            entity = new TruthMapEntity
            {
                SessionId = sessionId,
                CurrentState = serialized,
                Version = 1,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.TruthMaps.Add(entity);
        }
        else
        {
            entity.CurrentState = serialized;
            entity.Version++;
            entity.UpdatedAt = DateTime.UtcNow;
        }

        // Record patch event
        var patchEvent = new TruthMapPatchEvent
        {
            SessionId = sessionId,
            PatchJson = JsonSerializer.Serialize(patch, _jsonOptions),
            Agent = patch.Meta.Agent,
            Round = patch.Meta.Round,
            AppliedAt = DateTime.UtcNow
        };
        _dbContext.TruthMapPatchEvents.Add(patchEvent);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return updatedTruthMap;
    }

    public async Task SaveAsync(TruthMapModel truthMap, CancellationToken cancellationToken = default)
    {
        var serialized = JsonSerializer.Serialize(truthMap, _jsonOptions);

        var entity = await _dbContext.TruthMaps
            .FirstOrDefaultAsync(tm => tm.SessionId == truthMap.SessionId, cancellationToken);

        if (entity == null)
        {
            entity = new TruthMapEntity
            {
                SessionId = truthMap.SessionId,
                CurrentState = serialized,
                Version = truthMap.Version, // Use the version from the TruthMap parameter
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.TruthMaps.Add(entity);
        }
        else
        {
            entity.CurrentState = serialized;
            entity.Version = truthMap.Version; // Update to the version from the TruthMap parameter
            entity.UpdatedAt = DateTime.UtcNow;
            // Note: Version is NOT incremented - caller is responsible for setting correct version
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetImpactSetAsync(
        Guid sessionId,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        var truthMap = await GetAsync(sessionId, cancellationToken);
        if (truthMap == null)
        {
            return new HashSet<string>();
        }

        return ChangeImpactCalculator.GetImpactSet(entityId, truthMap);
    }

    public async Task<IReadOnlyList<TruthMapPatch>> GetPatchHistoryAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var events = await _dbContext.TruthMapPatchEvents
            .Where(e => e.SessionId == sessionId)
            .OrderBy(e => e.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var patches = new List<TruthMapPatch>();
        foreach (var evt in events)
        {
            try
            {
                var patch = JsonSerializer.Deserialize<TruthMapPatch>(evt.PatchJson, _jsonOptions);
                if (patch != null)
                {
                    patches.Add(patch);
                }
            }
            catch (JsonException)
            {
                // Skip malformed patches
            }
        }

        return patches;
    }

    /// <summary>
    /// Apply patch operations to Truth Map using JSON Pointer patch semantics.
    /// </summary>
    private TruthMapModel ApplyPatchOperations(TruthMapModel truthMap, TruthMapPatch patch)
    {
        var root = JsonSerializer.SerializeToNode(truthMap, _jsonOptions) as JsonObject
            ?? throw new InvalidOperationException("Failed to serialize Truth Map for patch application.");

        foreach (var operation in patch.Ops)
        {
            ApplyOperation(root, operation);
        }

        NormalizeOpenQuestions(root);

        var patched = root.Deserialize<TruthMapModel>(_jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize patched Truth Map.");

        return patched with
        {
            Version = truthMap.Version + 1,
            Round = Math.Max(truthMap.Round, patch.Meta.Round)
        };
    }

    /// <summary>
    /// Agent patch payloads can represent open questions in many shapes.
    /// Normalize to the strict OpenQuestion shape expected by the TruthMap model.
    /// Entries that cannot be normalized are removed so they cannot break deserialization.
    /// </summary>
    private static void NormalizeOpenQuestions(JsonObject root)
    {
        if (root["open_questions"] is not JsonArray questions)
        {
            return;
        }

        for (var i = 0; i < questions.Count;)
        {
            var entry = questions[i];
            if (entry is null)
            {
                questions.RemoveAt(i);
                continue;
            }

            if (entry is JsonValue scalarValue)
            {
                if (scalarValue.TryGetValue<string>(out var textValue) && !string.IsNullOrWhiteSpace(textValue))
                {
                    questions[i] = BuildNormalizedOpenQuestion(
                        id: null,
                        text: textValue,
                        blocking: false,
                        raisedBy: null);
                    i++;
                    continue;
                }

                // Non-string scalar (number/bool/etc.) is not a valid OpenQuestion payload.
                questions.RemoveAt(i);
                continue;
            }

            if (entry is not JsonObject questionObject)
            {
                questions.RemoveAt(i);
                continue;
            }

            var id = ReadString(questionObject, "id");
            var text = ReadString(questionObject, "text")
                ?? ReadString(questionObject, "question")
                ?? ReadString(questionObject, "prompt")
                ?? ReadString(questionObject, "description")
                ?? ReadString(questionObject, "content");
            var blocking = ReadBool(questionObject, "blocking")
                ?? ReadBool(questionObject, "is_blocking")
                ?? false;
            var raisedBy = ReadString(questionObject, "raised_by")
                ?? ReadString(questionObject, "raisedBy")
                ?? ReadString(questionObject, "agent")
                ?? ReadString(questionObject, "proposed_by");

            if (string.IsNullOrWhiteSpace(text))
            {
                // Unknown object shape: drop it instead of keeping invalid state.
                questions.RemoveAt(i);
                continue;
            }

            questions[i] = BuildNormalizedOpenQuestion(id, text, blocking, raisedBy);
            i++;
        }
    }

    private static JsonObject BuildNormalizedOpenQuestion(string? id, string text, bool blocking, string? raisedBy) =>
        new()
        {
            ["id"] = string.IsNullOrWhiteSpace(id) ? $"oq-{Guid.NewGuid():N}" : id,
            ["text"] = text,
            ["blocking"] = blocking,
            ["raised_by"] = string.IsNullOrWhiteSpace(raisedBy) ? "moderator" : raisedBy
        };

    private static string? ReadString(JsonObject source, string key)
    {
        if (!source.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        return node is JsonValue value && value.TryGetValue<string>(out var stringValue)
            ? stringValue
            : null;
    }

    private static bool? ReadBool(JsonObject source, string key)
    {
        if (!source.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue;
            }

            if (value.TryGetValue<string>(out var stringValue) && bool.TryParse(stringValue, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static void ApplyOperation(JsonObject root, PatchOperation operation)
    {
        var segments = ParsePath(operation.Path);
        if (segments.Count == 0)
        {
            throw new InvalidOperationException("Patch path cannot target the root object directly.");
        }

        var parent = NavigateToParent(root, segments);
        var target = segments[^1];
        var valueNode = operation.Value is null
            ? null
            : JsonSerializer.SerializeToNode(operation.Value);

        switch (operation.Op)
        {
            case PatchOp.Add:
                ApplyAdd(parent, target, valueNode);
                break;
            case PatchOp.Replace:
                ApplyReplace(parent, target, valueNode);
                break;
            case PatchOp.Remove:
                ApplyRemove(parent, target);
                break;
            default:
                throw new InvalidOperationException($"Unsupported patch operation: {operation.Op}");
        }
    }

    private static void ApplyAdd(JsonNode parent, string target, JsonNode? valueNode)
    {
        if (valueNode is null)
        {
            throw new InvalidOperationException("Add operation requires a non-null value.");
        }

        switch (parent)
        {
            case JsonObject parentObject:
                parentObject[target] = valueNode.DeepClone();
                break;
            case JsonArray parentArray:
                if (target == "-")
                {
                    parentArray.Add(valueNode.DeepClone());
                    break;
                }

                var addIndex = ParseArrayIndex(target, parentArray.Count, allowAppend: true);
                parentArray.Insert(addIndex, valueNode.DeepClone());
                break;
            default:
                throw new InvalidOperationException($"Unsupported parent node for add operation: {parent.GetType().Name}");
        }
    }

    private static void ApplyReplace(JsonNode parent, string target, JsonNode? valueNode)
    {
        if (valueNode is null)
        {
            throw new InvalidOperationException("Replace operation requires a non-null value.");
        }

        switch (parent)
        {
            case JsonObject parentObject:
                parentObject[target] = valueNode.DeepClone();
                break;
            case JsonArray parentArray:
                var replaceIndex = ParseArrayIndex(target, parentArray.Count, allowAppend: false);
                parentArray[replaceIndex] = valueNode.DeepClone();
                break;
            default:
                throw new InvalidOperationException($"Unsupported parent node for replace operation: {parent.GetType().Name}");
        }
    }

    private static void ApplyRemove(JsonNode parent, string target)
    {
        switch (parent)
        {
            case JsonObject parentObject:
                if (!parentObject.Remove(target))
                {
                    throw new InvalidOperationException($"Cannot remove property '{target}' because it does not exist.");
                }
                break;
            case JsonArray parentArray:
                var removeIndex = ParseArrayIndex(target, parentArray.Count, allowAppend: false);
                parentArray.RemoveAt(removeIndex);
                break;
            default:
                throw new InvalidOperationException($"Unsupported parent node for remove operation: {parent.GetType().Name}");
        }
    }

    private static JsonNode NavigateToParent(JsonObject root, IReadOnlyList<string> segments)
    {
        JsonNode current = root;

        for (var i = 0; i < segments.Count - 1; i++)
        {
            var token = segments[i];

            current = current switch
            {
                JsonObject currentObject => currentObject[token]
                    ?? throw new InvalidOperationException($"Path segment '{token}' does not exist."),
                JsonArray currentArray => currentArray[ParseArrayIndex(token, currentArray.Count, allowAppend: false)]
                    ?? throw new InvalidOperationException($"Path segment '{token}' resolved to null."),
                _ => throw new InvalidOperationException($"Path segment '{token}' cannot be traversed on {current.GetType().Name}.")
            };
        }

        return current;
    }

    private static List<string> ParsePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
        {
            throw new InvalidOperationException($"Invalid patch path '{path}'.");
        }

        return path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(DecodePointerToken)
            .ToList();
    }

    private static string DecodePointerToken(string token) =>
        token.Replace("~1", "/").Replace("~0", "~");

    private static int ParseArrayIndex(string token, int count, bool allowAppend)
    {
        if (!int.TryParse(token, out var index))
        {
            throw new InvalidOperationException($"Invalid array index token '{token}'.");
        }

        var max = allowAppend ? count : count - 1;
        if (index < 0 || index > max)
        {
            throw new InvalidOperationException($"Array index '{index}' is out of range.");
        }

        return index;
    }
}
