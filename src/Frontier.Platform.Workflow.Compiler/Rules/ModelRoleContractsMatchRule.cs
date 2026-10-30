using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// model-role.contracts-match (doc 13 §4.2; ADR-E27 D12): an <c>AgentTaskNode</c> bound to a
/// contract-bearing role names that role's input and output contracts, by exact id and major
/// (ADR-E2 D3: minors of one major are interchangeable). A role that declares none leaves the node
/// free to name its own. Unknown roles are <c>model-role.roles-exist</c>'s.
/// </summary>
public sealed class ModelRoleContractsMatchRule : IDefinitionValidationRule
{
    private readonly IAgentRoleCatalog _roles;

    /// <summary>Constructs the rule over the agent-role catalogue.</summary>
    public ModelRoleContractsMatchRule(IAgentRoleCatalog roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        _roles = roles;
    }

    public string RuleId => "model-role.contracts-match";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var roles = (await _roles.GetAgentRolesAsync(ct)).ToDictionary(r => r.RoleId, StringComparer.Ordinal);
        return [.. ctx.Definition.Nodes.OfType<AgentTaskNode>()
            .Where(node => roles.ContainsKey(node.Role))
            .SelectMany(node => Findings(node, roles[node.Role]))];
    }

    /// <summary>The mismatches between <paramref name="node"/>'s contracts and <paramref name="role"/>'s.</summary>
    internal IEnumerable<ValidationFinding> Findings(AgentTaskNode node, AgentRoleDescriptor role)
    {
        if (Mismatch(role.InputContractRef, node.InputContractType))
        {
            yield return Finding(node, role, "input_contract_type", role.InputContractRef!, node.InputContractType);
        }

        if (Mismatch(role.OutputContractRef, node.OutputContractType))
        {
            yield return Finding(node, role, "output_contract_type", role.OutputContractRef!, node.OutputContractType);
        }
    }

    /// <summary>Whether a role's declared ref and a node's named contract disagree on id or major. A role that declares nothing never disagrees.</summary>
    internal static bool Mismatch(string? declared, string named) =>
        declared is not null
        && (DataSchemaRefMatchRule.ParseSchemaRef(declared) is not { } roleRef
            || !DataSchemaRefMatchRule.IsSchemaRef(named)
            || DataSchemaRefMatchRule.ParseSchemaRef(named) is not { } nodeRef
            || roleRef.Id != nodeRef.Id
            || roleRef.Major != nodeRef.Major);

    private ValidationFinding Finding(AgentTaskNode node, AgentRoleDescriptor role, string field, string declared, string named) => new(
        RuleId, DefaultSeverity,
        $"role '{role.RoleId}' takes contract '{declared}' as its {(field.StartsWith("input", StringComparison.Ordinal) ? "input" : "output")}; the node names '{named}'. Name the same contract id and major.",
        node.NodeId, FieldPath: field);
}
