namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Structural self-checks for <see cref="WorkflowDefinition.Validate"/> (doc 13 §4.2:
/// <c>graph.is-dag</c> and related rules). Split out as its own internal class so each
/// check stays small and independently testable.
/// </summary>
internal static class WorkflowDefinitionValidator
{
    /// <summary>Every <see cref="WorkflowNode.NodeId"/> must be unique within the definition.</summary>
    internal static IEnumerable<string> ValidateUniqueNodeIds(IReadOnlyList<WorkflowNode> nodes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (!seen.Add(node.NodeId))
            {
                yield return $"Duplicate node id '{node.NodeId}'.";
            }
        }
    }

    /// <summary>Every <see cref="WorkflowEdge"/> must reference node ids present in <paramref name="nodes"/>.</summary>
    internal static IEnumerable<string> ValidateEdgesResolve(IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<WorkflowEdge> edges)
    {
        var nodeIds = nodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            if (!nodeIds.Contains(edge.FromNodeId))
            {
                yield return $"Edge references unknown from_node_id '{edge.FromNodeId}'.";
            }

            if (!nodeIds.Contains(edge.ToNodeId))
            {
                yield return $"Edge references unknown to_node_id '{edge.ToNodeId}'.";
            }
        }
    }

    /// <summary>The control-edge graph must be acyclic (doc 13 §4.2 <c>graph.is-dag</c>); loop bodies are node-internal, never back-edges.</summary>
    internal static IEnumerable<string> ValidateAcyclic(IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<WorkflowEdge> edges)
    {
        var adjacency = edges
            .Where(edge => edge.Kind == EdgeKind.Control)
            .GroupBy(edge => edge.FromNodeId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.ToNodeId).ToList());

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (CycleReachableFrom(node.NodeId, adjacency, visiting, visited))
            {
                yield return "Control-edge graph contains a cycle.";
                yield break;
            }
        }
    }

    internal static bool CycleReachableFrom(
        string nodeId,
        IReadOnlyDictionary<string, List<string>> adjacency,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (visited.Contains(nodeId))
        {
            return false;
        }

        if (!visiting.Add(nodeId))
        {
            return true;
        }

        if (adjacency.TryGetValue(nodeId, out var successors))
        {
            foreach (var successor in successors)
            {
                if (CycleReachableFrom(successor, adjacency, visiting, visited))
                {
                    return true;
                }
            }
        }

        visiting.Remove(nodeId);
        visited.Add(nodeId);
        return false;
    }
}
