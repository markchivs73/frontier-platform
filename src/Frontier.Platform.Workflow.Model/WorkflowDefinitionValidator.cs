using Frontier.Platform.Abstractions;

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

    /// <summary>
    /// A projection sits only on a data edge, names each target once, and gives every field exactly
    /// one source: a well-formed JSON Pointer or a non-null literal (ADR-E27 D14). Whether the
    /// projected shape fits the consumer is the compiler's <c>data.projection-compatible</c>.
    /// </summary>
    internal static IEnumerable<string> ValidateProjections(IReadOnlyList<WorkflowEdge> edges)
    {
        foreach (var edge in edges.Where(e => e.Projection is not null))
        {
            var label = $"Edge '{edge.FromNodeId}' -> '{edge.ToNodeId}'";
            if (edge.Kind != EdgeKind.Data)
            {
                yield return $"{label} is a control edge; only a data edge may declare a projection.";
            }

            foreach (var violation in ProjectionFieldViolations(edge.Projection!))
            {
                yield return $"{label}: {violation}";
            }
        }
    }

    /// <summary>The violations in one projection's fields.</summary>
    internal static IEnumerable<string> ProjectionFieldViolations(IReadOnlyList<ProjectionField> fields)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Target) || !targets.Add(field.Target))
            {
                yield return $"projection target '{field.Target}' is blank or repeated.";
            }

            if (SourceViolation(field) is { } violation)
            {
                yield return $"projection target '{field.Target}' {violation}";
            }
        }
    }

    /// <summary>Why a projection field's source is wrong, or null when it has exactly one good source.</summary>
    internal static string? SourceViolation(ProjectionField field) => (field.From, field.Literal) switch
    {
        (null, null) => "needs a source: set from or literal.",
        ({ }, { }) => "sets both from and literal; set one.",
        ({ } pointer, null) when !IsJsonPointer(pointer) => $"has from '{pointer}', which is not an RFC 6901 JSON Pointer.",
        (null, { ValueKind: System.Text.Json.JsonValueKind.Null }) => "has a null literal; omit the field instead.",
        _ => null,
    };

    /// <summary>Whether <paramref name="pointer"/> is RFC 6901: empty, or <c>/</c>-separated tokens where <c>~</c> is always <c>~0</c> or <c>~1</c>.</summary>
    internal static bool IsJsonPointer(string pointer)
    {
        if (pointer.Length > 0 && pointer[0] != '/')
        {
            return false;
        }

        for (var i = 0; i < pointer.Length; i++)
        {
            if (pointer[i] == '~' && (i + 1 == pointer.Length || pointer[i + 1] is not ('0' or '1')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Embedded contracts are each well-formed and sorted by ref with no repeats (ADR-E27 D15), so the
    /// definition hash does not depend on the order publish found them in.
    /// </summary>
    internal static IEnumerable<string> ValidateEmbeddedContracts(IReadOnlyList<ContractSnapshot>? contracts)
    {
        string? previous = null;
        foreach (var contract in contracts ?? [])
        {
            foreach (var violation in SnapshotViolations(contract))
            {
                yield return $"Embedded contract '{contract.ContractRef}': {violation}";
            }

            if (previous is not null && string.CompareOrdinal(previous, contract.ContractRef) >= 0)
            {
                yield return $"Embedded contracts must be sorted by ref with no repeats; '{contract.ContractRef}' follows '{previous}'.";
            }

            previous = contract.ContractRef;
        }
    }

    /// <summary>The violations <see cref="ContractSnapshot.Validate"/> reports for <paramref name="contract"/>.</summary>
    internal static IReadOnlyList<string> SnapshotViolations(ContractSnapshot contract)
    {
        try
        {
            contract.Validate();
            return [];
        }
        catch (ContractViolationException ex)
        {
            return ex.Violations;
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
