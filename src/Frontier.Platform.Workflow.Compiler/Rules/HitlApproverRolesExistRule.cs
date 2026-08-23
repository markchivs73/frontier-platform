
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// hitl.approver-roles-exist (doc 13 §4.2 R2, doc 06 §3, S9.30): every
/// <see cref="HumanGateNode.ApproverRoles"/> entry must exist in the approver role catalogue
/// (<see cref="IApproverRoleCatalog"/>) — the same catalogue the chat designer agent's proposals
/// are constrained to, so a published gate can never wait on a role nobody holds.
/// </summary>
public sealed class HitlApproverRolesExistRule : IDefinitionValidationRule
{
    private readonly IApproverRoleCatalog _approverRoles;

    /// <summary>Constructs the rule over the approver role catalogue.</summary>
    public HitlApproverRolesExistRule(IApproverRoleCatalog approverRoles)
    {
        ArgumentNullException.ThrowIfNull(approverRoles);
        _approverRoles = approverRoles;
    }

    public string RuleId => "hitl.approver-roles-exist";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var roles = await _approverRoles.GetApproverRolesAsync(ct);
        var knownRoleIds = roles.Select(r => r.RoleId).ToHashSet(StringComparer.Ordinal);

        return ctx.Definition.Nodes.OfType<HumanGateNode>()
            .SelectMany(gate => gate.ApproverRoles
                .Where(role => !knownRoleIds.Contains(role))
                .Select(role => new ValidationFinding(RuleId, DefaultSeverity,
                    $"approver_role '{role}' does not exist in the approver role catalogue.",
                    gate.NodeId, FieldPath: "approver_roles")))
            .ToList();
    }
}
