using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The shape every role definition must have, whatever its backing (ADR-PA40): a well-formed role
/// id, requirements on a model-backed role, and both contract refs on an agent-backed one. Enforced
/// when a stored definition is read, so a document edited outside this library cannot reach the
/// catalogue. Whether a ref resolves to a registered contract is the governance service's check.
/// </summary>
internal static class RoleDefinitionShape
{
    /// <summary>Returns <paramref name="definition"/>, or throws a permanent <see cref="ContractViolationException"/> listing every violation.</summary>
    internal static RoleDefinition EnsureValid(RoleDefinition definition)
    {
        var violations = Violations(definition);
        return violations.Count == 0
            ? definition
            : throw new ContractViolationException(nameof(RoleDefinition), violations);
    }

    /// <summary>Every shape violation in <paramref name="definition"/>.</summary>
    internal static List<string> Violations(RoleDefinition definition)
    {
        var violations = new List<string>();
        if (!RoleIdFormat.IsValid(definition.RoleId))
        {
            violations.Add($"role id '{definition.RoleId}' must be lowercase kebab-case of at most {RoleIdFormat.MaxLength} characters (ADR-PA40).");
        }

        if (definition.Backing == RoleBacking.Model && definition.Requirements is null)
        {
            violations.Add($"model-backed role '{definition.RoleId}' must state its capability requirements (ADR-PA40).");
        }

        if (definition.Backing == RoleBacking.Agent
            && (string.IsNullOrWhiteSpace(definition.InputContractRef) || string.IsNullOrWhiteSpace(definition.OutputContractRef)))
        {
            violations.Add($"agent-backed role '{definition.RoleId}' must declare both an input and an output contract ref (ADR-PA40).");
        }

        return violations;
    }
}
