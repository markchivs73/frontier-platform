using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// structure.unique-node-ids: every NodeId within the definition must be unique, and every edge's
/// <c>from_node_id</c> and <c>to_node_id</c> must name a node in the definition (S13.207). Doc 13
/// §4.2, Phase 1 rule catalogue. Ids are compared ordinally: ids that differ only by case name different nodes.
/// </summary>
public sealed class StructureUniqueNodeIdsRule : PureTierRule
{
    /// <inheritdoc />
    public override string RuleId => "structure.unique-node-ids";

    /// <inheritdoc />
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var findings = new List<ValidationFinding>();

        foreach (var node in ctx.Definition.Nodes)
        {
            if (!seen.Add(node.NodeId))
            {
                findings.Add(new ValidationFinding(
                    RuleId,
                    DefaultSeverity,
                    $"Duplicate node id '{node.NodeId}'.",
                    NodeId: node.NodeId));
            }
        }

        findings.AddRange(ctx.Definition.Edges.SelectMany(edge => DanglingEndFindings(edge, seen)));
        return findings;
    }

    /// <summary>A finding for each end of <paramref name="edge"/> that names no node in <paramref name="nodeIds"/>.</summary>
    private IEnumerable<ValidationFinding> DanglingEndFindings(WorkflowEdge edge, HashSet<string> nodeIds)
    {
        if (!nodeIds.Contains(edge.FromNodeId))
        {
            yield return DanglingFinding(edge, "from_node_id", edge.FromNodeId);
        }

        if (!nodeIds.Contains(edge.ToNodeId))
        {
            yield return DanglingFinding(edge, "to_node_id", edge.ToNodeId);
        }
    }

    private ValidationFinding DanglingFinding(WorkflowEdge edge, string field, string nodeId) => new(
        RuleId,
        DefaultSeverity,
        $"Edge references unknown {field} '{nodeId}'; no node in the definition has that id.",
        EdgeRef: $"{edge.FromNodeId}->{edge.ToNodeId}",
        FieldPath: field);
}
