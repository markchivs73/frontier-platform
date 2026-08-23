
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// graph.single-entry-reachable: the control-edge graph must have exactly one entry node
/// (no incoming control edges), and all nodes must be reachable from it.
/// Doc 13 §4.2, Phase 1 rule catalogue.
/// </summary>
public sealed class GraphSingleEntryReachableRule : PureTierRule
{
    public override string RuleId => "graph.single-entry-reachable";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var nodes = ctx.Definition.Nodes;
        if (nodes.Count == 0) return Array.Empty<ValidationFinding>();

        var controlEdges = ctx.Definition.Edges
            .Where(e => e.Kind == EdgeKind.Control)
            .ToList();

        var hasIncoming = controlEdges
            .Select(e => e.ToNodeId)
            .ToHashSet(StringComparer.Ordinal);

        var entryNodes = nodes
            .Where(n => !hasIncoming.Contains(n.NodeId))
            .ToList();

        if (entryNodes.Count != 1)
        {
            return new[]
            {
                new ValidationFinding(
                    RuleId,
                    DefaultSeverity,
                    $"Graph must have exactly one entry node (found {entryNodes.Count}).")
            };
        }

        var adjacency = controlEdges
            .GroupBy(e => e.FromNodeId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ToNodeId).ToList());

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(entryNodes[0].NodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!reachable.Add(current)) continue;
            if (adjacency.TryGetValue(current, out var successors))
                foreach (var s in successors) queue.Enqueue(s);
        }

        var unreachable = nodes.Where(n => !reachable.Contains(n.NodeId)).ToList();
        if (unreachable.Count > 0)
        {
            return unreachable
                .Select(n => new ValidationFinding(
                    RuleId,
                    DefaultSeverity,
                    $"Node '{n.NodeId}' is unreachable from the entry node.",
                    NodeId: n.NodeId))
                .ToArray();
        }

        return Array.Empty<ValidationFinding>();
    }
}
