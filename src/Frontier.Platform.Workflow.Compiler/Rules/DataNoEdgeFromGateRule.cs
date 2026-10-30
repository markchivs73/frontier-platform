using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// data.no-edge-from-gate (doc 13 §4.2; ADR-E27 D13): a data edge leaving a <c>human_gate</c> carries
/// the gate's own output, <c>frontier/gate-decision/1.0</c>, and nothing else. Before D13 a gate wrote
/// no output and such an edge threw at run time; the rule kept its id when D13 shipped.
/// </summary>
public sealed class DataNoEdgeFromGateRule : PureTierRule
{
    public override string RuleId => "data.no-edge-from-gate";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var gates = ctx.Definition.Nodes.OfType<HumanGateNode>().Select(g => g.NodeId).ToHashSet(StringComparer.Ordinal);
        return ctx.Definition.Edges
            .Where(e => e.Kind == EdgeKind.Data && gates.Contains(e.FromNodeId) && !string.Equals(e.ContractType, GateDecision.ContractRef, StringComparison.Ordinal))
            .Select(e => new ValidationFinding(RuleId, DefaultSeverity,
                $"a data edge from gate '{e.FromNodeId}' must carry '{GateDecision.ContractRef}', the gate's decision; it names '{e.ContractType}'.",
                e.FromNodeId, EdgeRef: $"{e.FromNodeId}->{e.ToNodeId}", FieldPath: "contract_type"))
            .ToList();
    }
}
