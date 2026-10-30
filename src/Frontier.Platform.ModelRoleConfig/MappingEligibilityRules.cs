namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The rules a mapping must meet against its role's current definition (ADR-PA40, ADR-E27 D16):
/// the chain's kind is the role's backing, an agent-backed role's mapping carries well-formed
/// evaluation evidence, and evidence is refused once the role's contract hashes have moved past the
/// ones the proposal stamped. Pure, so every branch is reachable without a store; each failure is a
/// typed <see cref="MappingGovernanceRefusalException"/>.
/// </summary>
internal static class MappingEligibilityRules
{
    /// <summary>Throws <see cref="UnknownRoleDefinitionException"/> when <paramref name="definition"/> is absent.</summary>
    internal static RoleDefinition EnsureDefined(RoleDefinition? definition, string roleId) =>
        definition ?? throw new UnknownRoleDefinitionException(
            nameof(RoleDefinition), [$"role '{roleId}' has no current definition; define the role before proposing a mapping for it (ADR-PA40)."]);

    /// <summary>Throws unless <paramref name="mapping"/>'s chain kind and evidence suit <paramref name="role"/>.</summary>
    internal static void EnsureEligible(RoleDefinition role, RoleMapping mapping)
    {
        var kind = KindOf(mapping.Chain);
        if (kind != role.Backing)
        {
            throw new ChainBackingMismatchException(
                nameof(MappingChangeProposal),
                [$"role '{role.RoleId}' is {role.Backing.Name}-backed, so a chain of {kind.Name}s cannot serve it (ADR-PA40)."]);
        }

        EnsureEvidence(role, mapping.EvaluationEvidenceRef);
    }

    /// <summary>
    /// Evidence is required for an agent-backed role and optional for a model-backed one (Q4, S4-11);
    /// a ref that is supplied must be well-formed whatever the backing, so a stored ref always parses.
    /// </summary>
    internal static void EnsureEvidence(RoleDefinition role, string? evidenceRef)
    {
        if (evidenceRef is null && role.Backing == RoleBacking.Model)
        {
            return;
        }

        if (!EvaluationEvidence.IsWellFormed(evidenceRef))
        {
            var found = evidenceRef is null ? "none was given" : $"'{evidenceRef}' is not of that form";
            throw new EvaluationEvidenceRequiredException(
                nameof(MappingChangeProposal),
                [$"a mapping for role '{role.RoleId}' needs evaluation_evidence_ref of the form eval-run:{{manual|kit}}:{{id}}; {found} (ADR-E27 D16)."]);
        }
    }

    /// <summary>
    /// Refuses evidence the role has outgrown (D16): a proposal that carries evidence must still match
    /// the role's current contract hashes, since a new contract version re-opens eligibility. A
    /// proposal without evidence (a model-backed role's, by S4-11) has nothing to go stale.
    /// </summary>
    internal static void EnsureNotStale(RoleDefinition role, RoleMapping mapping)
    {
        if (mapping.EvaluationEvidenceRef is null || EvaluationEvidence.CoversCurrentContracts(role, mapping))
        {
            return;
        }

        throw new EvaluationEvidenceStaleException(
            nameof(MappingChangeProposal),
            [$"role '{role.RoleId}' is now at definition v{role.DefinitionVersion}, whose contract hashes are not the ones evidence "
             + $"'{mapping.EvaluationEvidenceRef}' was proposed against; propose again with evidence for the current contracts (ADR-E27 D16)."]);
    }

    /// <summary><paramref name="mapping"/> stamped with <paramref name="role"/>'s current contract hashes; a caller's hashes are never kept.</summary>
    internal static RoleMapping Stamp(RoleMapping mapping, RoleDefinition role) => mapping with
    {
        InputContractHash = role.InputContractHash,
        OutputContractHash = role.OutputContractHash,
    };

    /// <summary>A chain's kind: agents when it holds an agent entry, else models (a chain is never mixed, ADR-PA27).</summary>
    internal static RoleBacking KindOf(IReadOnlyList<ChainEntry> chain) =>
        chain.Any(entry => entry is AgentEntry) ? RoleBacking.Agent : RoleBacking.Model;
}
