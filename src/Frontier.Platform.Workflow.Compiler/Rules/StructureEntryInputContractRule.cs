using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// structure.entry-input-contract (doc 13 §4.2, doc 14 §3, S13.209): a one-shot definition's entry
/// <c>agent_task</c> (no incoming edge of either kind) is handed the deployment's entry payload at runtime,
/// not an upstream data payload, so it must declare the entry contract from
/// <see cref="IEntryContractCatalog"/> as its <c>input_contract_type</c> and request the entry
/// dynamic field in <c>context_request.dynamic_fields</c>. The design agent is told this from the
/// same catalogue; until this rule, nothing checked the proposal obeyed it. Dispatcher-mode entry
/// nodes take the work-item contract instead and stay under <c>structure.dispatcher-input</c>;
/// an entry that is not an <c>agent_task</c> is outside this rule. A node fed by a Data edge is not an
/// entry even with no control edge into it: the runtime hands it that edge's payload, so a chain built
/// from Data edges alone has one entry, not one per node.
/// </summary>
public sealed class StructureEntryInputContractRule : IDefinitionValidationRule
{
    private readonly IEntryContractCatalog _entryContracts;

    /// <summary>Constructs the rule over the deployment's entry-contract catalogue.</summary>
    public StructureEntryInputContractRule(IEntryContractCatalog entryContracts)
    {
        ArgumentNullException.ThrowIfNull(entryContracts);
        _entryContracts = entryContracts;
    }

    /// <inheritdoc />
    public string RuleId => "structure.entry-input-contract";

    /// <inheritdoc />
    public RuleTier Tier => RuleTier.Resourced;

    /// <inheritdoc />
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Definition.Mode != ExecutionMode.OneShot) return [];

        var entry = await _entryContracts.GetEntryContractAsync(ct);
        var fed = ctx.Definition.Edges.Select(e => e.ToNodeId).ToHashSet(StringComparer.Ordinal);
        return ctx.Definition.Nodes.OfType<AgentTaskNode>()
            .Where(agent => !fed.Contains(agent.NodeId))
            .SelectMany(agent => EntryFindings(agent, entry))
            .ToList();
    }

    private IEnumerable<ValidationFinding> EntryFindings(AgentTaskNode agent, EntryContractDescriptor entry)
    {
        if (!string.Equals(agent.InputContractType, entry.ContractTypeName, StringComparison.Ordinal))
        {
            yield return new(RuleId, DefaultSeverity,
                $"The entry node is handed {entry.Description} at runtime, so input_contract_type must be '{entry.ContractTypeName}', not '{agent.InputContractType}'.",
                agent.NodeId, FieldPath: "input_contract_type");
        }

        if (!(agent.ContextRequest?.DynamicFields ?? []).Contains(entry.DynamicFieldName, StringComparer.Ordinal))
        {
            yield return new(RuleId, DefaultSeverity,
                $"The entry node must request '{entry.DynamicFieldName}' in context_request.dynamic_fields; that field carries {entry.Description}.",
                agent.NodeId, FieldPath: "context_request.dynamic_fields");
        }
    }
}
