
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// data.contract-types-resolve (doc 13 §4.2 R2, S9.30): every CLR contract name a Data edge's
/// <c>contract_type</c> or an agent node's input/output contract carries must resolve in the contract
/// type catalogue — an unresolvable name would fail live at deserialization (hit for real at S9.28,
/// where 7 contract types were missing from the registry). A schema ref
/// (<c>{ns}/{name}/{major}.{minor}</c>, ADR-E2) is not a CLR name: ADR-E27 leaves it to
/// <c>data.schema-ref-resolves</c> on both edges and nodes.
/// </summary>
public sealed class DataContractTypesResolveRule : IDefinitionValidationRule
{
    private readonly IContractTypeCatalog _contracts;

    /// <summary>Constructs the rule over the contract type catalogue.</summary>
    public DataContractTypesResolveRule(IContractTypeCatalog contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        _contracts = contracts;
    }

    public string RuleId => "data.contract-types-resolve";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var findings = EdgeFindings(ctx.Definition).Concat(NodeFindings(ctx.Definition)).ToList();
        return Task.FromResult<IReadOnlyList<ValidationFinding>>(findings);
    }

    private IEnumerable<ValidationFinding> EdgeFindings(WorkflowDefinition definition) =>
        definition.Edges
            .Where(e => e.Kind == EdgeKind.Data && Unresolved(e.ContractType))
            .Select(e => new ValidationFinding(RuleId, DefaultSeverity,
                $"data edge contract_type '{e.ContractType}' does not resolve in the contract registry.",
                e.ToNodeId, EdgeRef: $"{e.FromNodeId}->{e.ToNodeId}", FieldPath: "contract_type"));

    private IEnumerable<ValidationFinding> NodeFindings(WorkflowDefinition definition)
    {
        foreach (var node in definition.Nodes.OfType<AgentTaskNode>())
        {
            if (Unresolved(node.InputContractType))
            {
                yield return ContractFinding(node.NodeId, "input_contract_type", node.InputContractType);
            }

            if (Unresolved(node.OutputContractType))
            {
                yield return ContractFinding(node.NodeId, "output_contract_type", node.OutputContractType);
            }
        }
    }

    /// <summary>
    /// True when <paramref name="contractType"/> is a CLR name the catalogue does not know; a schema ref
    /// is never unresolved here (data.schema-ref-resolves governs it).
    /// </summary>
    private bool Unresolved(string? contractType) =>
        !DataSchemaRefMatchRule.IsSchemaRef(contractType) && !_contracts.Resolves(contractType ?? string.Empty);

    private ValidationFinding ContractFinding(string nodeId, string fieldPath, string contractType) => new(
        RuleId, DefaultSeverity,
        $"{fieldPath} '{contractType}' does not resolve in the contract registry.",
        nodeId, FieldPath: fieldPath);
}
