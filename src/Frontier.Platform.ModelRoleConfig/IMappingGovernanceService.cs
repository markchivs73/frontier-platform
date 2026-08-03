namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The change-governance loop for role→model mappings (doc 08 §3, §7-8): propose →
/// shadow → evidence → approve → canary → fleet, plus instant rollback (ADR-M3). S4.3
/// registers <c>MappingGovernanceService</c>, an explicit stub (per the approved S4.3
/// plan) — full shadow/canary evaluation and the rollback REST surface are future work
/// once the audit trail (Stage 5) can supply evaluation evidence.
/// </summary>
public interface IMappingGovernanceService
{
    /// <summary>Records a proposed mapping change for a role, pending approval (doc 08 §7).</summary>
    Task<MappingChangeProposal> ProposeChangeAsync(MappingChange change, CancellationToken cancellationToken);

    /// <summary>Approves a previously proposed change, advancing it into its mapping's rollout ring (doc 08 §7).</summary>
    Task ApproveAsync(string proposalId, string approverId, CancellationToken cancellationToken);

    /// <summary>Instantly reverts a role's active mapping to a prior version (doc 08 §8 ADR-M3) — no proposal required.</summary>
    Task RollbackAsync(string roleId, int toVersion, string reason, CancellationToken cancellationToken);
}
