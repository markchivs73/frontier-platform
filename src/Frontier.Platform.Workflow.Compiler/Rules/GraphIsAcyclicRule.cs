
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// graph.is-acyclic: the control-edge graph must be a DAG; self-loops and back-edges are forbidden.
/// Doc 13 §4.2, Phase 1 rule catalogue.
/// </summary>
public sealed class GraphIsAcyclicRule : PureTierRule
{
    public override string RuleId => "graph.is-acyclic";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var adjacency = ctx.Definition.Edges
            .Where(e => e.Kind == EdgeKind.Control)
            .GroupBy(e => e.FromNodeId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ToNodeId).ToList());

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in ctx.Definition.Nodes)
        {
            if (HasCycle(node.NodeId, adjacency, visiting, visited))
            {
                return new[]
                {
                    new ValidationFinding(RuleId, DefaultSeverity, "Control-edge graph contains a cycle.")
                };
            }
        }

        return Array.Empty<ValidationFinding>();
    }

    private static bool HasCycle(
        string nodeId,
        IReadOnlyDictionary<string, List<string>> adjacency,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (visited.Contains(nodeId)) return false;
        if (!visiting.Add(nodeId)) return true;

        if (adjacency.TryGetValue(nodeId, out var successors))
        {
            foreach (var successor in successors)
            {
                if (HasCycle(successor, adjacency, visiting, visited))
                    return true;
            }
        }

        visiting.Remove(nodeId);
        visited.Add(nodeId);
        return false;
    }
}
