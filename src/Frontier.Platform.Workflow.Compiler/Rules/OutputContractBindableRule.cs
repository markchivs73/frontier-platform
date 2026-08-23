
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// Resourced-tier rule (doc 13 §4.2 R2, ADR-DC7, S13.7h): every <see cref="AgentTaskNode"/>'s
/// output contract must be bindable as an LLM structured-output schema — every object in the
/// exported schema closed with <c>additionalProperties: false</c>.
///
/// An open map (<c>Dictionary&lt;string, T&gt;</c>) cannot be, and the provider rejects the whole
/// request. That is not theoretical: a designed workflow declared
/// <c>EngagementProgressProjection</c> — an internal projection carrying a dictionary — as a node's
/// output, validated clean, and then failed mid-run with
/// "output_config.format.schema: For 'object' type, 'additionalProperties' must be explicitly set
/// to false". <c>data.contract-types-resolve</c> only proves the name resolves; this proves the
/// type is usable as an agent output.
/// </summary>
public sealed class OutputContractBindableRule : IDefinitionValidationRule
{
    private readonly IContractTypeCatalog _contracts;

    public OutputContractBindableRule(IContractTypeCatalog contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        _contracts = contracts;
    }

    public string RuleId => "data.output-contract-bindable";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        IReadOnlyList<ValidationFinding> findings = ctx.Definition.Nodes
            .OfType<AgentTaskNode>()
            .Select(node => (node, openMap: OpenMapPath(node.OutputContractType)))
            .Where(x => x.openMap is not null)
            .Select(x => NotBindableFinding(x.node, x.openMap!))
            .ToList();

        return Task.FromResult(findings);
    }

    /// <summary>The open-map path in the named contract, or null when it resolves and is bindable. Unknown names are left to <c>data.contract-types-resolve</c>.</summary>
    private string? OpenMapPath(string contractTypeName) =>
        _contracts.Resolve(contractTypeName) is { } type ? StrictSchemaCheck.FirstOpenMapPath(type) : null;

    private ValidationFinding NotBindableFinding(AgentTaskNode node, string openMapPath) => new(
        RuleId: RuleId,
        Severity: DefaultSeverity,
        Message: $"output_contract_type '{node.OutputContractType}' cannot be an agent output: the model provider requires every object in a "
               + $"structured-output schema to set additionalProperties to false, and this type has an open map at '{openMapPath}'. "
               + "Choose a flat step-output contract instead.",
        NodeId: node.NodeId,
        FieldPath: "output_contract_type");
}
