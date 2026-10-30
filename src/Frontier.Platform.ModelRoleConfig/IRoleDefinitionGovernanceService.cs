namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The governance loop for role definitions (ADR-PA40, consumer ADR-E27 D12): propose, then approve,
/// reject or withdraw. It mirrors <see cref="IMappingGovernanceService"/> without the rings — there is
/// no promote and no rollback; to revert, propose the old definition again.
/// <para>
/// Every decision is recorded through <see cref="IMappingDecisionRecorder"/> <b>before</b> it is made,
/// under the <see cref="RoleDefinitionGovernanceEventTypes"/>, and compensated if it then fails (K6,
/// fail closed). Contract refs resolve through the consumer-owned
/// <see cref="Abstractions.IContractSnapshotCatalog"/>; approval stamps their hashes.
/// </para>
/// </summary>
public interface IRoleDefinitionGovernanceService
{
    /// <summary>Records a proposed new role, or a change to one, pending approval.</summary>
    /// <param name="definition">The proposed definition. Its version, approver, effective-at and contract hashes are ignored — the service stamps them.</param>
    /// <param name="proposedBy">The authenticated principal proposing it (ADR-E8).</param>
    /// <param name="reason">Why; required.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InvalidRoleDefinitionException">The definition is ill-formed (400).</exception>
    /// <exception cref="UnknownRoleContractException">A contract ref is not registered, or is retired (400).</exception>
    /// <exception cref="RoleBackingChangeException">The proposal changes an existing role's backing (409).</exception>
    /// <exception cref="MappingProposalAlreadyPendingException">The role already holds a definition proposal awaiting a decision (409).</exception>
    Task<RoleDefinitionProposal> ProposeAsync(RoleDefinition definition, string proposedBy, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Approves a proposal: resolves its contract refs again and stamps their hashes, allocates the
    /// next definition version, stores it and moves the role's definition pointer. The approver may
    /// never be the proposer.
    /// </summary>
    /// <param name="expectedConcurrencyToken">The token last read, or <see langword="null"/> to decide without a guard; a stale one is refused and never retried (ADR-PA34).</param>
    /// <exception cref="UnknownMappingProposalException">The role holds no definition proposal by that id (404).</exception>
    /// <exception cref="IllegalMappingTransitionException">The proposal is already decided (409).</exception>
    /// <exception cref="DistinctApproverRequiredException">The approver is the proposer (403).</exception>
    /// <exception cref="MappingConcurrencyConflictException">The supplied token is stale (409).</exception>
    /// <exception cref="UnknownRoleContractException">A contract ref was retired or removed since propose (400).</exception>
    /// <exception cref="MappingGovernanceException">The allocation kept losing to concurrent writers (503).</exception>
    Task<RoleDefinitionProposal> ApproveAsync(
        string roleId, string proposalId, string approverId, string reason, string? expectedConcurrencyToken, CancellationToken cancellationToken);

    /// <summary>Refuses a proposal. Terminal. The rejector may never be the proposer.</summary>
    /// <exception cref="DistinctApproverRequiredException">The rejector is the proposer (403) — withdraw instead.</exception>
    /// <exception cref="MappingConcurrencyConflictException">The supplied token is stale (409).</exception>
    Task<RoleDefinitionProposal> RejectAsync(
        string roleId, string proposalId, string approverId, string reason, string? expectedConcurrencyToken, CancellationToken cancellationToken);

    /// <summary>Retracts an undecided proposal. Terminal. Only the proposer may withdraw.</summary>
    /// <exception cref="ProposerOnlyWithdrawalException">The actor did not raise this proposal (403).</exception>
    /// <exception cref="MappingConcurrencyConflictException">The supplied token is stale (409).</exception>
    Task<RoleDefinitionProposal> WithdrawAsync(
        string roleId, string proposalId, string actor, string reason, string? expectedConcurrencyToken, CancellationToken cancellationToken);

    /// <summary>A role's definition proposal, carrying its concurrency token, or <see langword="null"/> when it has none by that id.</summary>
    Task<RoleDefinitionProposal?> GetProposalAsync(string roleId, string proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// One page of definition proposals, most recently proposed first, filtered by role and a set of
    /// states exactly as the mapping list is (doc 20 §Role definitions). With no role it runs cross-partition.
    /// </summary>
    Task<RoleDefinitionProposalPage> ListProposalsAsync(MappingProposalQuery query, CancellationToken cancellationToken);
}
