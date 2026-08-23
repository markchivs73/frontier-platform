
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// graph.fan-out-fan-in (doc 13 §4.2 R3): every <see cref="ParallelNode"/> fan-out's branches
/// converge at its declared join — branch and join ids reference existing nodes, and the join is
/// reachable from every branch over control edges (no branch escapes or dead-ends).
/// </summary>
public sealed class GraphFanOutFanInRule : PureTierRule
{
    public override string RuleId => "graph.fan-out-fan-in";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var nodeIds = ctx.Definition.Nodes.Select(n => n.NodeId).ToHashSet(StringComparer.Ordinal);
        var adjacency = ControlGraphWalker.BuildControlAdjacency(ctx.Definition);

        return ctx.Definition.Nodes.OfType<ParallelNode>()
            .SelectMany(parallel => ParallelFindings(parallel, nodeIds, adjacency))
            .ToList();
    }

    private IEnumerable<ValidationFinding> ParallelFindings(
        ParallelNode parallel,
        HashSet<string> nodeIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> adjacency)
    {
        if (!nodeIds.Contains(parallel.JoinNodeId))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"join_node_id '{parallel.JoinNodeId}' does not reference an existing node.",
                parallel.NodeId, FieldPath: "join_node_id");
            yield break;
        }

        if (parallel.BranchNodeIds.Count == 0)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                "parallel node declares no branches.", parallel.NodeId, FieldPath: "branch_node_ids");
        }

        foreach (var branch in parallel.BranchNodeIds)
        {
            if (!nodeIds.Contains(branch))
            {
                yield return new ValidationFinding(RuleId, DefaultSeverity,
                    $"branch_node_id '{branch}' does not reference an existing node.",
                    parallel.NodeId, FieldPath: "branch_node_ids");
            }
            else if (!ControlGraphWalker.IsReachable(adjacency, branch, parallel.JoinNodeId))
            {
                yield return new ValidationFinding(RuleId, DefaultSeverity,
                    $"branch '{branch}' does not converge at the declared join '{parallel.JoinNodeId}'.",
                    parallel.NodeId, FieldPath: "branch_node_ids");
            }
        }
    }
}
