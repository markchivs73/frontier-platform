namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Storage for proposals and for concurrency-safe mapping-version allocation (ADR-PA32), kept behind
/// a port so the governance flow is unit-tested without the Cosmos SDK —
/// <see cref="CosmosMappingProposalStore"/> is the adapter.
/// <para>
/// <b>Internal on purpose.</b> <see cref="IRoleRegistry"/> is shipped and implemented by consumers'
/// test doubles, so adding version listing and allocation to it would be a source break for every
/// implementor (the ADR-PA29 reasoning that produced <see cref="IMappingPinner"/>, taken one step
/// further: nothing outside this library needs to implement this, so nothing outside it should see it).
/// </para>
/// </summary>
internal interface IMappingProposalStore
{
    /// <summary>Every mapping version stored for <paramref name="roleId"/>, ascending. Empty when the role has none.</summary>
    Task<IReadOnlyList<int>> ListMappingVersionsAsync(string roleId, CancellationToken cancellationToken);

    /// <summary>The stored mapping version, or <see langword="null"/> when it does not exist.</summary>
    Task<RoleMapping?> FindMappingVersionAsync(string roleId, int version, CancellationToken cancellationToken);

    /// <summary>The version <c>current</c> points at, or <see langword="null"/> before the role has a pointer.</summary>
    Task<int?> FindCurrentVersionAsync(string roleId, CancellationToken cancellationToken);

    /// <summary>
    /// The role's proposal that is still awaiting a decision, or <see langword="null"/>. A role may
    /// hold only one at a time (Mark's call, 2026-09-16), and this is how <c>ProposeAsync</c> enforces it.
    /// </summary>
    Task<StoredMappingProposal?> FindUndecidedProposalAsync(string roleId, CancellationToken cancellationToken);

    /// <summary>The stored proposal and its concurrency token, or <see langword="null"/>.</summary>
    Task<StoredMappingProposal?> FindProposalAsync(string roleId, string proposalId, CancellationToken cancellationToken);

    /// <summary>Stores a new proposal. The id is deterministic, so a duplicate create conflicts rather than forking.</summary>
    Task CreateProposalAsync(MappingChangeProposal proposal, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces <paramref name="proposal"/> if it still carries <paramref name="expectedETag"/>. Returns
    /// <see langword="false"/> when another decision won the race.
    /// </summary>
    Task<bool> TryReplaceProposalAsync(MappingChangeProposal proposal, string expectedETag, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically creates the mapping-version document for <paramref name="mapping"/>, replaces
    /// <paramref name="proposal"/> on the condition it still carries <paramref name="expectedETag"/>, and —
    /// when <paramref name="makeCurrent"/> is true — repoints <c>current</c> at the new version.
    /// <para>
    /// The version document's id is <c>{roleId}:v{n}</c> and the create is <b>create-only</b>, so two
    /// approvals that both computed the same <c>n</c> cannot both land: the loser gets a conflict and
    /// re-reads. That create is the allocation — there is no separate counter to disagree with it.
    /// </para>
    /// </summary>
    /// <returns><see langword="false"/> on a concurrency conflict; the caller re-reads, re-allocates and retries.</returns>
    Task<bool> TryAllocateVersionAsync(
        RoleMapping mapping,
        MappingChangeProposal proposal,
        string expectedETag,
        bool makeCurrent,
        CancellationToken cancellationToken);

    /// <summary>
    /// The proposal whose <b>live</b> version — its promoted fleet version, else its approved canary
    /// version — is <paramref name="version"/>, or <see langword="null"/>. A point query rather than a
    /// scan, so a rollback finds the owning proposal however many the role has accumulated.
    /// </summary>
    Task<StoredMappingProposal?> FindProposalByLiveVersionAsync(string roleId, int version, CancellationToken cancellationToken);

    /// <summary>One page of a role's proposals, most recently proposed first.</summary>
    Task<MappingProposalPage> QueryProposalsAsync(MappingProposalQuery query, CancellationToken cancellationToken);
}

/// <summary>A stored proposal with the concurrency token its next decision must match.</summary>
internal sealed record StoredMappingProposal(MappingChangeProposal Proposal, string ETag);
