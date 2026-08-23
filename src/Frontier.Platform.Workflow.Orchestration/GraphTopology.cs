using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Computes a stable node-execution order from a <see cref="WorkflowDefinition"/>'s
/// Control and Data edges (doc 00 §4, S2.2). Deliberately separate from CascadeLogic's
/// <c>ArtifactGraphDerivation</c> (library-boundaries: subsystem libraries don't reference
/// each other) — this operates on node ids over all edges, not section keys over Data
/// edges only.
/// </summary>
internal static class GraphTopology
{
    /// <summary>Topological order of every node id in <paramref name="definition"/>, via Kahn's algorithm with lexicographic tie-breaking.</summary>
    internal static IReadOnlyList<string> ExecutionOrder(WorkflowDefinition definition)
    {
        var nodeIds = definition.Nodes.Select(node => node.NodeId).ToList();
        var edges = BuildAdjacency(definition.Edges, nodeIds);

        return TopologicalSort(edges, nodeIds);
    }

    /// <summary>Builds the node-id adjacency map from every edge, regardless of <see cref="EdgeKind"/>.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildAdjacency(IReadOnlyList<WorkflowEdge> edges, IReadOnlyList<string> nodeIds)
    {
        var adjacency = nodeIds.ToDictionary(id => id, _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            adjacency[edge.FromNodeId].Add(edge.ToNodeId);
        }

        return adjacency.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToList(), StringComparer.Ordinal);
    }

    /// <summary>Kahn's algorithm: repeatedly takes the lexicographically smallest ready node, for a stable order.</summary>
    internal static IReadOnlyList<string> TopologicalSort(IReadOnlyDictionary<string, IReadOnlyList<string>> edges, IReadOnlyList<string> nodeIds)
    {
        var inDegree = ComputeInDegrees(edges, nodeIds);
        var ready = new SortedSet<string>(nodeIds.Where(id => inDegree[id] == 0), StringComparer.Ordinal);
        var order = new List<string>();

        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            order.Add(next);
            ReleaseSuccessors(edges, inDegree, ready, next);
        }

        if (order.Count != nodeIds.Count)
        {
            throw new ContractViolationException(nameof(WorkflowDefinition), ["Node graph contains a cycle; cannot compute an execution order."]);
        }

        return order;
    }

    /// <summary>Computes each node's in-degree (number of direct upstream edges).</summary>
    internal static Dictionary<string, int> ComputeInDegrees(IReadOnlyDictionary<string, IReadOnlyList<string>> edges, IReadOnlyList<string> nodeIds)
    {
        var inDegree = nodeIds.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);

        foreach (var targets in edges.Values)
        {
            foreach (var target in targets)
            {
                inDegree[target]++;
            }
        }

        return inDegree;
    }

    /// <summary>Decrements the in-degree of every direct successor of <paramref name="node"/>, queuing any that become ready.</summary>
    internal static void ReleaseSuccessors(IReadOnlyDictionary<string, IReadOnlyList<string>> edges, Dictionary<string, int> inDegree, SortedSet<string> ready, string node)
    {
        foreach (var target in edges[node])
        {
            if (--inDegree[target] == 0)
            {
                ready.Add(target);
            }
        }
    }
}
