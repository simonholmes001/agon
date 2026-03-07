using Agon.Domain.TruthMap.Entities;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Domain.Engines;

/// <summary>
/// Computes the downstream blast radius of a change to any entity in the Truth Map.
///
/// Algorithm:
/// 1. Start from the changed entity ID.
/// 2. Traverse the derived_from graph: find all entities whose derived_from list
///    includes the changed entity (directly or transitively).
/// 3. Return the complete set of affected entity IDs.
///
/// The Orchestrator uses this to mark affected entities as PendingRevalidation
/// and to dispatch targeted micro-round tasks to the relevant agents.
/// </summary>
public static class ChangeImpactCalculator
{
    /// <summary>
    /// Returns the set of entity IDs that are transitively derived from
    /// <paramref name="changedEntityId"/>.
    /// The changed entity itself is NOT included in the result.
    /// </summary>
    public static IReadOnlySet<string> GetImpactSet(string changedEntityId, TruthMapModel map)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(changedEntityId);
        ArgumentNullException.ThrowIfNull(map);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(changedEntityId);

        // Build an index: entityId → list of entity IDs that declare it in derived_from.
        var dependents = BuildDependentsIndex(map);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!dependents.TryGetValue(current, out var children))
                continue;

            foreach (var child in children.Where(c => visited.Add(c)))
                queue.Enqueue(child);
        }

        return visited;
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a map of entityId → [ids of entities that list it in their derived_from].
    /// This inverts the derived_from relationship so we can traverse downstream.
    /// </summary>
    private static Dictionary<string, List<string>> BuildDependentsIndex(TruthMapModel map)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        AddToIndex(index, map.Claims.Select(c => (c.Id, c.DerivedFrom)));
        AddToIndex(index, map.Assumptions.Select(a => (a.Id, a.DerivedFrom)));
        AddToIndex(index, map.Decisions.Select(d => (d.Id, d.DerivedFrom)));
        AddToIndex(index, map.Risks.Select(r => (r.Id, r.DerivedFrom)));

        return index;
    }

    private static void AddToIndex(
        Dictionary<string, List<string>> index,
        IEnumerable<(string Id, IReadOnlyList<string> DerivedFrom)> entities)
    {
        foreach (var (id, derivedFrom) in entities)
        {
            foreach (var parentId in derivedFrom)
            {
                if (!index.TryGetValue(parentId, out var deps))
                {
                    deps = new List<string>();
                    index[parentId] = deps;
                }

                deps.Add(id);
            }
        }
    }
}
