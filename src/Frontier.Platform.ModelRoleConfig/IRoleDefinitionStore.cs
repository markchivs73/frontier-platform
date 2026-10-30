namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Storage for role-definition proposals and concurrency-safe definition-version allocation
/// (ADR-PA40), behind a port so the governance flow is unit-tested without the Cosmos SDK —
/// <see cref="CosmosRoleDefinitionStore"/> is the adapter. Internal for the reason
/// <see cref="IMappingProposalStore"/> is: nothing outside this library implements it.
/// </summary>
internal interface IRoleDefinitionStore
{
    /// <summary>The role's current definition, or <see langword="null"/> before it has one.</summary>
    Task<RoleDefinition?> FindCurrentDefinitionAsync(string roleId, CancellationToken cancellationToken);

    /// <summary>Every stored definition version for <paramref name="roleId"/>, ascending. Empty when the role has none.</summary>
    Task<IReadOnlyList<int>> ListDefinitionVersionsAsync(string roleId, CancellationToken cancellationToken);

    /// <summary>The role's definition proposal still awaiting a decision, or <see langword="null"/>. One at a time per role.</summary>
    Task<StoredRoleDefinitionProposal?> FindUndecidedProposalAsync(string roleId, CancellationToken cancellationToken);

    /// <summary>The stored proposal and its concurrency token, or <see langword="null"/>.</summary>
    Task<StoredRoleDefinitionProposal?> FindProposalAsync(string roleId, string proposalId, CancellationToken cancellationToken);

    /// <summary>Stores a new proposal. The id is deterministic, so a duplicate create conflicts rather than forking.</summary>
    Task CreateProposalAsync(RoleDefinitionProposal proposal, CancellationToken cancellationToken);

    /// <summary>Replaces <paramref name="proposal"/> if it still carries <paramref name="expectedETag"/>.</summary>
    /// <returns>The stored document's new concurrency token, or <see langword="null"/> when another decision won.</returns>
    Task<string?> TryReplaceProposalAsync(RoleDefinitionProposal proposal, string expectedETag, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically creates <c>{roleId}:definition:v{n}</c> for <paramref name="definition"/> (create-only,
    /// so the create is the allocation), replaces <paramref name="proposal"/> on the condition it still
    /// carries <paramref name="expectedETag"/>, and repoints <c>{roleId}:definition:current</c> at it.
    /// </summary>
    /// <returns>The proposal's new concurrency token, or <see langword="null"/> on a concurrency conflict.</returns>
    Task<string?> TryAllocateDefinitionAsync(
        RoleDefinition definition, RoleDefinitionProposal proposal, string expectedETag, CancellationToken cancellationToken);

    /// <summary>One page of definition proposals, most recently proposed first.</summary>
    Task<RoleDefinitionProposalPage> QueryProposalsAsync(MappingProposalQuery query, CancellationToken cancellationToken);
}

/// <summary>A stored definition proposal with the concurrency token its next decision must match.</summary>
internal sealed record StoredRoleDefinitionProposal(RoleDefinitionProposal Proposal, string ETag);
