
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// Resourced-tier rule (doc 13 §4.2 R3, ADR-DC7, S13.7h): every node's type must be one the
/// deployment's orchestrator can actually execute (<see cref="IExecutableNodeTypeCatalog"/>).
///
/// Without it a definition using an unimplemented node type validates clean, publishes, and then
/// fails permanently on its first run — which is exactly what happened: a designed workflow
/// carrying a <c>parallel</c> node passed validation with zero findings and died at execution with
/// "GraphOrchestrator supports only 'agent_task'/'human_gate' nodes". Resourced rather than pure
/// because the supported set is a property of the deployed runtime, not of the definition.
/// </summary>
public sealed class NodeTypeSupportedRule : IDefinitionValidationRule
{
    private readonly IExecutableNodeTypeCatalog _catalog;

    public NodeTypeSupportedRule(IExecutableNodeTypeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public string RuleId => "structure.node-type-supported";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        IReadOnlyList<ValidationFinding> findings = ctx.Definition.Nodes
            .Where(node => !_catalog.IsExecutable(node.NodeType))
            .Select(UnsupportedFinding)
            .ToList();

        return Task.FromResult(findings);
    }

    private ValidationFinding UnsupportedFinding(WorkflowNode node) => new(
        RuleId: RuleId,
        Severity: DefaultSeverity,
        Message: $"node_type '{node.NodeType.Name}' is not executable by this runtime, so the workflow would fail on its first run; "
               + $"executable node types are: {string.Join(", ", _catalog.ExecutableNodeTypeNames)}",
        NodeId: node.NodeId,
        FieldPath: "node_type");
}
