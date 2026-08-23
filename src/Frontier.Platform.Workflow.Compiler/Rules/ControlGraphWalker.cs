
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// Shared control-edge graph traversal for validation rules (S9.30): adjacency construction,
/// reachability, and entry-node detection over <see cref="EdgeKind.Control"/> edges only.
/// </summary>
internal static class ControlGraphWalker
{
    /// <summary>Builds a control-edge adjacency map: from-node id → successor node ids.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildControlAdjacency(WorkflowDefinition definition) =>
        definition.Edges
            .Where(e => e.Kind == EdgeKind.Control)
            .GroupBy(e => e.FromNodeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(e => e.ToNodeId).ToList(), StringComparer.Ordinal);

    /// <summary>Breadth-first reachability from <paramref name="fromNodeId"/> to <paramref name="toNodeId"/> over control edges. A node is considered reachable from itself.</summary>
    internal static bool IsReachable(IReadOnlyDictionary<string, IReadOnlyList<string>> adjacency, string fromNodeId, string toNodeId)
    {
        var queue = new Queue<string>([fromNodeId]);
        var seen = new HashSet<string>(StringComparer.Ordinal) { fromNodeId };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (string.Equals(current, toNodeId, StringComparison.Ordinal)) return true;

            foreach (var next in adjacency.GetValueOrDefault(current, []).Where(seen.Add))
            {
                queue.Enqueue(next);
            }
        }

        return false;
    }

    /// <summary>Node ids with no incoming control edge — the definition's entry candidates.</summary>
    internal static IReadOnlyList<string> FindEntryNodeIds(WorkflowDefinition definition)
    {
        var targets = definition.Edges
            .Where(e => e.Kind == EdgeKind.Control)
            .Select(e => e.ToNodeId)
            .ToHashSet(StringComparer.Ordinal);

        return definition.Nodes.Select(n => n.NodeId).Where(id => !targets.Contains(id)).ToList();
    }
}
