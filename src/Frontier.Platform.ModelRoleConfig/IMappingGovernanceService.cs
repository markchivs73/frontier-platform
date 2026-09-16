namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The change-governance loop for role→model mappings (doc 08 §3, §7-8; ADR-PA32): propose →
/// approve (canary) → promote (fleet), with reject and withdraw, plus instant rollback (ADR-M3).
/// <para>
/// Every decision is recorded through <see cref="IMappingDecisionRecorder"/> <b>before</b> it is made,
/// so a decision whose audit record cannot be written does not happen (K6, fail closed). Approval
/// allocates the next mapping version under a create-only guard, so two concurrent approvals can
/// never both write <c>{role}:v{n}</c>.
/// </para>
/// <para>
/// Shadow evaluation is deferred (S13.101): an approval goes straight to the canary ring, and a
/// shadow version never becomes <c>current</c>.
/// </para>
/// </summary>
public interface IMappingGovernanceService
{
    /// <summary>Records a proposed mapping change for a role, pending approval (doc 08 §7).</summary>
    /// <param name="change">The proposed change. Its ring, version, approver and effective-at are ignored — the service stamps them.</param>
    /// <param name="proposedBy">The authenticated principal proposing it (ADR-E8). The consumer's API requires <c>model-governance</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="Abstractions.ContractViolationException">The change is not proposable — no reason, an ill-shaped chain, or an unresolvable target (K7).</exception>
    Task<MappingChangeProposal> ProposeAsync(MappingChange change, string proposedBy, CancellationToken cancellationToken);

    /// <summary>
    /// Approves a proposal: allocates its mapping version, stores it, and repoints <c>current</c> so the
    /// new mapping is live in the canary ring (doc 08 §7). The approver may never be the proposer.
    /// </summary>
    /// <exception cref="Abstractions.ContractViolationException">Unknown proposal, an illegal transition, or the proposer approving their own change.</exception>
    /// <exception cref="MappingGovernanceException">The allocation kept losing to concurrent approvals.</exception>
    Task<MappingChangeProposal> ApproveAsync(string roleId, string proposalId, string approverId, string reason, CancellationToken cancellationToken);

    /// <summary>Refuses a proposal. Terminal (doc 08 §7).</summary>
    Task<MappingChangeProposal> RejectAsync(string roleId, string proposalId, string approverId, string reason, CancellationToken cancellationToken);

    /// <summary>Retracts an undecided proposal. Terminal (doc 08 §7).</summary>
    Task<MappingChangeProposal> WithdrawAsync(string roleId, string proposalId, string actor, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Promotes an approved mapping from canary to fleet. Manual only — there is no auto-promotion.
    /// Because a mapping version is immutable and its ring is part of it, promotion appends a new
    /// fleet version carrying the same chain rather than rewriting the canary one.
    /// </summary>
    Task<MappingChangeProposal> PromoteAsync(string roleId, string proposalId, string actor, string reason, CancellationToken cancellationToken);

    /// <summary>A role's proposal, or <see langword="null"/> when it has none by that id.</summary>
    Task<MappingChangeProposal?> GetProposalAsync(string roleId, string proposalId, CancellationToken cancellationToken);

    /// <summary>One page of a role's proposals, most recently proposed first.</summary>
    Task<MappingProposalPage> ListProposalsAsync(MappingProposalQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Instantly reverts a role's active mapping to a prior version (doc 08 §8 ADR-M3) — no approval
    /// required. The target is <b>required</b>, must exist, and must not be a shadow version. Takes
    /// effect for new executions immediately; in-flight executions stay pinned (ADR-PA29).
    /// </summary>
    /// <exception cref="Abstractions.ContractViolationException">The target version does not exist, or is a shadow version that has never served.</exception>
    Task<MappingRollbackResult> RollbackToVersionAsync(string roleId, int toVersion, string actor, string reason, CancellationToken cancellationToken);

    /// <summary>Records a proposed mapping change for a role, pending approval (doc 08 §7).</summary>
    [Obsolete("A proposal must be attributed to the principal that made it (ADR-E8). Use ProposeAsync(change, proposedBy, cancellationToken). This overload has never had a working implementation — it has thrown since S4.3.")]
    Task<MappingChangeProposal> ProposeChangeAsync(MappingChange change, CancellationToken cancellationToken);

    /// <summary>Approves a previously proposed change, advancing it into its mapping's rollout ring (doc 08 §7).</summary>
    [Obsolete("An approval needs its role (proposals are partitioned by role) and a reason for the governance record. Use ApproveAsync(roleId, proposalId, approverId, reason, cancellationToken). This overload has never had a working implementation — it has thrown since S4.3.")]
    Task ApproveAsync(string proposalId, string approverId, CancellationToken cancellationToken);

    /// <summary>Instantly reverts a role's active mapping to a prior version (doc 08 §8 ADR-M3) — no proposal required.</summary>
    [Obsolete("A rollback must be attributed and audited, and must refuse a missing or shadow target (ADR-PA32). Use RollbackToVersionAsync(roleId, toVersion, actor, reason, cancellationToken), which also returns what changed.")]
    Task RollbackAsync(string roleId, int toVersion, string reason, CancellationToken cancellationToken);
}
