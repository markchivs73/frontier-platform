
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// graph.decision-edges (doc 13 §4.2 R3): every <see cref="DecisionNode"/> out-edge carries a
/// condition (the default-branch edge is the exempt fall-through), and an out-edge to the declared
/// <see cref="DecisionNode.DefaultBranchNodeId"/> exists — no unreachable fall-through.
/// </summary>
public sealed class GraphDecisionEdgesRule : PureTierRule
{
    public override string RuleId => "graph.decision-edges";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx) =>
        ctx.Definition.Nodes.OfType<DecisionNode>()
            .SelectMany(decision => DecisionFindings(decision, ctx.Definition.Edges))
            .ToList();

    private IEnumerable<ValidationFinding> DecisionFindings(DecisionNode decision, IReadOnlyList<WorkflowEdge> edges)
    {
        var outEdges = edges
            .Where(e => e.Kind == EdgeKind.Control && string.Equals(e.FromNodeId, decision.NodeId, StringComparison.Ordinal))
            .ToList();

        foreach (var edge in outEdges.Where(e =>
                     string.IsNullOrWhiteSpace(e.Condition) &&
                     !string.Equals(e.ToNodeId, decision.DefaultBranchNodeId, StringComparison.Ordinal)))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"decision out-edge to '{edge.ToNodeId}' carries no condition.",
                decision.NodeId, EdgeRef: $"{edge.FromNodeId}->{edge.ToNodeId}");
        }

        if (!outEdges.Any(e => string.Equals(e.ToNodeId, decision.DefaultBranchNodeId, StringComparison.Ordinal)))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"no out-edge reaches the declared default branch '{decision.DefaultBranchNodeId}'.",
                decision.NodeId, FieldPath: "default_branch_node_id");
        }
    }
}
