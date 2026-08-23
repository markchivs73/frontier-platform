
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// Resourced-tier rule (doc 13 §4.2, S9.27c): every <c>AgentTaskNode.Role</c> must exist in the
/// agent-role catalogue (<see cref="IAgentRoleCatalog"/>, S9.27) — the same catalogue the chat
/// designer agent's system prompt is constrained to, so a published definition can never reference
/// a role the agent was never allowed to invent either.
/// </summary>
public sealed class ModelRoleRolesExistRule : IDefinitionValidationRule
{
    private readonly IAgentRoleCatalog _agentRoleCatalog;

    public ModelRoleRolesExistRule(IAgentRoleCatalog agentRoleCatalog)
    {
        ArgumentNullException.ThrowIfNull(agentRoleCatalog);
        _agentRoleCatalog = agentRoleCatalog;
    }

    public string RuleId => "model-role.roles-exist";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var roles = await _agentRoleCatalog.GetAgentRolesAsync(ct);
        var knownRoleIds = roles.Select(r => r.RoleId).ToHashSet(StringComparer.Ordinal);

        return ctx.Definition.Nodes
            .OfType<AgentTaskNode>()
            .Where(node => !knownRoleIds.Contains(node.Role))
            .Select(UnresolvedRoleFinding)
            .ToList();
    }

    private ValidationFinding UnresolvedRoleFinding(AgentTaskNode node) => new(
        RuleId: RuleId,
        Severity: DefaultSeverity,
        Message: $"role '{node.Role}' does not exist in the agent-role catalogue",
        NodeId: node.NodeId,
        FieldPath: "role");
}
