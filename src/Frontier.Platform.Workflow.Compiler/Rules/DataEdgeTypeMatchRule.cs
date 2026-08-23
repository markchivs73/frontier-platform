
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// data.edge-type-match (doc 13 §4.2 R3, S9.30): a Data edge's payload deserializes as the
/// consumer node's declared input contract (proven the hard way at S9.28's UpdateTicketInput
/// reshape), so the edge's <c>contract_type</c> must equal the consuming agent node's
/// <c>input_contract_type</c>. Edges with an unresolvable/absent contract type are
/// <c>data.contract-types-resolve</c>'s findings, not duplicated here. This is the <b>sole</b>
/// authority for edge contract-type matching — the cascade guardian no longer duplicates it
/// (S9.70); this finding is anchored (NodeId + EdgeRef + FieldPath) for the UI.
/// </summary>
public sealed class DataEdgeTypeMatchRule : IDefinitionValidationRule
{
    public string RuleId => "data.edge-type-match";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var agentsById = ctx.Definition.Nodes.OfType<AgentTaskNode>().ToDictionary(n => n.NodeId, StringComparer.Ordinal);
        var findings = ctx.Definition.Edges
            // S13.7d: schema-ref edges (ADR-E2) match by exact-id + major in data.schema-ref-match,
            // where minors may differ — exact string equality here would be wrongly stricter.
            .Where(e => e.Kind == EdgeKind.Data && !string.IsNullOrWhiteSpace(e.ContractType) && !DataSchemaRefMatchRule.IsSchemaRef(e.ContractType))
            .SelectMany(e => MismatchFinding(e, agentsById))
            .ToList();

        return Task.FromResult<IReadOnlyList<ValidationFinding>>(findings);
    }

    private IEnumerable<ValidationFinding> MismatchFinding(WorkflowEdge edge, Dictionary<string, AgentTaskNode> agentsById)
    {
        if (agentsById.TryGetValue(edge.ToNodeId, out var consumer) &&
            !string.Equals(edge.ContractType, consumer.InputContractType, StringComparison.Ordinal))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"data edge carries '{edge.ContractType}' but consumer '{consumer.NodeId}' declares input contract '{consumer.InputContractType}'.",
                consumer.NodeId, EdgeRef: $"{edge.FromNodeId}->{edge.ToNodeId}", FieldPath: "contract_type");
        }
    }
}
