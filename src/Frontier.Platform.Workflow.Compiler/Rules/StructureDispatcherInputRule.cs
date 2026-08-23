
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// structure.dispatcher-input (doc 13 §4.2, ADR-E8 / doc 00 §4.4): a Dispatcher-mode definition's
/// work items deserialize as its entry node's input contract, so every entry node (no incoming
/// control edge) must be an agent_task declaring a non-empty <c>input_contract_type</c>. OneShot
/// definitions have no WorkItem wait surface in Phase 1 (no wait-node type exists), so the OneShot
/// half of the doc row has nothing to check yet — recorded in docs/state/SPEC-TRACEABILITY.md.
/// </summary>
public sealed class StructureDispatcherInputRule : PureTierRule
{
    public override string RuleId => "structure.dispatcher-input";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        if (ctx.Definition.Mode != ExecutionMode.Dispatcher) return [];

        var nodesById = ctx.Definition.Nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);
        return ControlGraphWalker.FindEntryNodeIds(ctx.Definition)
            .Select(id => EntryFinding(nodesById[id]))
            .OfType<ValidationFinding>()
            .ToList();
    }

    private ValidationFinding? EntryFinding(WorkflowNode entry) => entry switch
    {
        AgentTaskNode agent when !string.IsNullOrWhiteSpace(agent.InputContractType) => null,
        AgentTaskNode agent => new(RuleId, DefaultSeverity,
            "Dispatcher-mode entry node must declare the work-item input contract in input_contract_type.",
            agent.NodeId, FieldPath: "input_contract_type"),
        _ => new(RuleId, DefaultSeverity,
            "Dispatcher-mode definitions must start at an agent_task node that declares the work-item input contract.",
            entry.NodeId),
    };
}
