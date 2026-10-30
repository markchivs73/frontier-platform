namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The rules a role definition proposal must satisfy (ADR-PA40). Pure, so every branch is reachable
/// without a store. Each failure is a typed <see cref="MappingGovernanceRefusalException"/>, so it is
/// permanent and never retried (invariant 7) and a consumer maps its status code by type.
/// </summary>
internal static class RoleDefinitionProposalRules
{
    /// <summary>The states a definition proposal may hold; it never enters shadow, promoted or rolled back.</summary>
    private static readonly MappingProposalState[] DefinitionStates =
        [MappingProposalState.PendingApproval, MappingProposalState.Approved, MappingProposalState.Rejected, MappingProposalState.Withdrawn];

    /// <summary>Throws <see cref="InvalidRoleDefinitionException"/> unless <paramref name="definition"/> may be proposed.</summary>
    internal static void ValidateDefinition(RoleDefinition definition)
    {
        var violations = RoleDefinitionShape.Violations(definition);
        if (string.IsNullOrWhiteSpace(definition.Description))
        {
            violations.Add($"role '{definition.RoleId}' needs a description; the designer matches node intent against it (ADR-PA40).");
        }

        AddRefViolation(definition.InputContractRef, "input_contract_ref", violations);
        AddRefViolation(definition.OutputContractRef, "output_contract_ref", violations);
        Throw(violations);
    }

    /// <summary>A declared ref must be an ADR-E2 schema ref; an undeclared one is the backing rule's concern.</summary>
    internal static void AddRefViolation(string? contractRef, string field, List<string> violations)
    {
        if (contractRef is not null && !ContractRefFormat.IsValid(contractRef))
        {
            violations.Add($"{field} '{contractRef}' is not an ADR-E2 schema ref ({{namespace}}/{{name}}/{{major}}.{{minor}}).");
        }
    }

    /// <summary>
    /// Backing is declared once and immutable (ADR-PA40): a proposal that changes an existing role's
    /// backing is refused, because moving between model and agent means a new role.
    /// </summary>
    internal static void EnsureBackingUnchanged(RoleDefinition? current, RoleDefinition proposed)
    {
        if (current is not null && current.Backing != proposed.Backing)
        {
            throw new RoleBackingChangeException(
                nameof(RoleDefinition),
                [$"role '{proposed.RoleId}' is {current.Backing.Name}-backed and its backing cannot change to {proposed.Backing.Name}; define a new role instead (ADR-PA40)."]);
        }
    }

    /// <summary>Throws unless <paramref name="proposal"/> is internally consistent.</summary>
    internal static void ValidateProposal(RoleDefinitionProposal proposal)
    {
        var violations = new List<string>();
        AddRequired(proposal.ProposalId, "proposal_id is required.", violations);
        AddRequired(proposal.ProposedBy, "proposed_by is required; a proposal is attributed to the principal that made it (ADR-E8).", violations);
        AddRequired(proposal.Reason, "reason is required; a role definition is a governed change, not an edit (ADR-PA40).", violations);
        if (!string.Equals(proposal.RoleId, proposal.ProposedDefinition.RoleId, StringComparison.Ordinal))
        {
            violations.Add($"the proposed definition is for role '{proposal.ProposedDefinition.RoleId}', but the proposal names '{proposal.RoleId}'.");
        }

        AddStateViolations(proposal, violations);
        Throw(violations);
    }

    /// <summary>A definition proposal holds only the four definition states, and an approved one carries its stamps.</summary>
    internal static void AddStateViolations(RoleDefinitionProposal proposal, List<string> violations)
    {
        if (Array.IndexOf(DefinitionStates, proposal.State) < 0)
        {
            violations.Add($"a role-definition proposal never enters '{proposal.State.Name}'; definitions have no promote and no rollback (ADR-PA40).");
        }

        if (proposal.State == MappingProposalState.Approved && (proposal.DefinitionVersion is null || string.IsNullOrWhiteSpace(proposal.ApprovedBy)))
        {
            violations.Add("an approved proposal must carry the definition_version allocated for it and name its approver.");
        }
    }

    /// <summary>Adds <paramref name="message"/> when <paramref name="value"/> is blank.</summary>
    internal static void AddRequired(string value, string message, List<string> violations)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            violations.Add(message);
        }
    }

    /// <summary>
    /// <paramref name="definition"/> with every approval stamp removed: a caller's version, approver,
    /// effective-at and hashes are never trusted (ADR-PA32's rule for stamped fields).
    /// </summary>
    internal static RoleDefinition Unstamped(RoleDefinition definition) => definition with
    {
        InputContractHash = null,
        OutputContractHash = null,
        DefinitionVersion = null,
        ApprovedBy = null,
        EffectiveFromUtc = null,
    };

    /// <summary>Throws <see cref="InvalidRoleDefinitionException"/> listing <paramref name="violations"/>, if there are any.</summary>
    internal static void Throw(List<string> violations)
    {
        if (violations.Count > 0)
        {
            throw new InvalidRoleDefinitionException(nameof(RoleDefinitionProposal), violations);
        }
    }
}
