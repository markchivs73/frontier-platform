using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Hitl;

/// <summary>
/// Writes <see cref="ApprovalRequest"/> documents to the <c>approvals</c> container
/// (doc 02 §3, doc 06 §9). A read-optimised projection, never orchestration truth —
/// DTF history remains authoritative. Upserts are convergent: a retried call with the
/// same <see cref="ApprovalRequest.Id"/> and <see cref="ApprovalRequest.Status"/>
/// reproduces the same document, so this store is safe under DTF activity retry.
/// </summary>
public interface IApprovalStore
{
    /// <summary>Upserts <paramref name="request"/> into the <c>approvals</c> container.</summary>
    Task UpsertAsync(ApprovalRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Doc 06 §9's one non-append-only write: the ETag-guarded transition of the request
    /// <paramref name="approvalId"/> in partition <paramref name="engagementId"/> from
    /// <see cref="ApprovalRequestStatus.Pending"/> or <see cref="ApprovalRequestStatus.Escalated"/>
    /// to <see cref="ApprovalRequestStatus.Decided"/>, with <paramref name="decision"/> embedded.
    /// Exactly one decision wins: a request already decided is left as it stands and comes back as
    /// <see cref="ApprovalDecisionOutcome.AlreadyDecided"/> with the winning record; an unknown id is
    /// <see cref="ApprovalDecisionOutcome.NotFound"/>. The caller raises the orchestration's gate event
    /// only after <see cref="ApprovalDecisionOutcome.Decided"/>, in that order, so doc 12 §8's recovery
    /// sweep can re-raise a decision whose raise was lost.
    /// </summary>
    Task<ApprovalDecisionResult> DecideAsync(string engagementId, string approvalId, HitlDecision decision, CancellationToken cancellationToken);

    /// <summary>
    /// The doc 12 §8 recovery-sweep's gate-reraise check: every
    /// <see cref="ApprovalRequest"/> whose <see cref="ApprovalRequest.Status"/> is
    /// <see cref="ApprovalRequestStatus.Decided"/>, across all engagements.
    /// </summary>
    Task<IReadOnlyList<ApprovalRequest>> GetDecidedAsync(CancellationToken cancellationToken);
}
