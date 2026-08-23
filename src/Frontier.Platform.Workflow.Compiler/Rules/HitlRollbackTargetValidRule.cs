
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// hitl.rollback-target-valid (doc 13 §4.2 R3, doc 06 §3): each gate's rollback target must
/// reference an existing node, sit upstream of the gate on the control spine, and produce a
/// section (carry <c>artifact_key</c>) so an approved snapshot can exist to restore. A null
/// target means reject-in-place (revise &amp; resubmit) and is valid.
/// </summary>
public sealed class HitlRollbackTargetValidRule : PureTierRule
{
    public override string RuleId => "hitl.rollback-target-valid";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var nodesById = ctx.Definition.Nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);
        var adjacency = ControlGraphWalker.BuildControlAdjacency(ctx.Definition);

        return ctx.Definition.Nodes.OfType<HumanGateNode>()
            .Where(gate => gate.RollbackToNodeId is not null)
            .SelectMany(gate => GateFindings(gate, nodesById, adjacency))
            .ToList();
    }

    private IEnumerable<ValidationFinding> GateFindings(
        HumanGateNode gate,
        Dictionary<string, WorkflowNode> nodesById,
        IReadOnlyDictionary<string, IReadOnlyList<string>> adjacency)
    {
        var targetId = gate.RollbackToNodeId!;
        if (!nodesById.TryGetValue(targetId, out var target))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"rollback_to_node_id '{targetId}' does not reference an existing node.",
                gate.NodeId, FieldPath: "rollback_to_node_id");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(target.ArtifactKey))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"rollback target '{targetId}' produces no section (no artifact_key) — nothing to restore an approved snapshot for.",
                gate.NodeId, FieldPath: "rollback_to_node_id");
        }

        if (string.Equals(targetId, gate.NodeId, StringComparison.Ordinal) ||
            !ControlGraphWalker.IsReachable(adjacency, targetId, gate.NodeId))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"rollback target '{targetId}' is not upstream of the gate on the control spine.",
                gate.NodeId, FieldPath: "rollback_to_node_id");
        }
    }
}
