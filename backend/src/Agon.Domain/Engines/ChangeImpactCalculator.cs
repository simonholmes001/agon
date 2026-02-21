using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;

namespace Agon.Domain.Engines;

/// <summary>
/// Traverses the derived_from graph to find all entities transitively
/// impacted when a given entity changes. Used for change propagation.
/// </summary>
public static class ChangeImpactCalculator
{
    /// <summary>
    /// Returns the set of entity IDs that are transitively impacted by a change
    /// to the entity with the given ID. Does not include the source entity itself.
    /// Handles circular dependencies safely.
    /// </summary>
    public static IReadOnlySet<string> CalculateImpact(string entityId, TruthMapState map)
    {
        var impacted = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(entityId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var dependents = FindDependents(current, map);

            foreach (var dependent in dependents)
            {
                if (impacted.Add(dependent))
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        // Don't include the source entity
        impacted.Remove(entityId);
        return impacted;
    }

    /// <summary>
    /// Finds all entities that have the given entityId in their derived_from,
    /// supports, or contradicts lists.
    /// </summary>
    private static IEnumerable<string> FindDependents(string entityId, TruthMapState map)
    {
        return map.Claims.Where(c => c.DerivedFrom.Contains(entityId)).Select(c => c.Id)
            .Concat(map.Assumptions.Where(a => a.DerivedFrom.Contains(entityId)).Select(a => a.Id))
            .Concat(map.Decisions.Where(d => d.DerivedFrom.Contains(entityId)).Select(d => d.Id))
            .Concat(map.Risks.Where(r => r.DerivedFrom.Contains(entityId)).Select(r => r.Id))
            .Concat(map.Evidence.Where(e => e.Supports.Contains(entityId) || e.Contradicts.Contains(entityId)).Select(e => e.Id));
    }
}
