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
    /// Exactly one decision wins: a request already decided — or no longer awaiting a decision, as an
    /// <see cref="ApprovalRequestStatus.Expired"/> or <see cref="ApprovalRequestStatus.Withdrawn"/> one —
    /// is left as it stands and comes back as
    /// <see cref="ApprovalDecisionOutcome.AlreadyDecided"/> with the stored record; an unknown id is
    /// <see cref="ApprovalDecisionOutcome.NotFound"/>. The caller raises the orchestration's gate event
    /// only after <see cref="ApprovalDecisionOutcome.Decided"/>, in that order, so doc 12 §8's recovery
    /// sweep can re-raise a decision whose raise was lost.
    /// </summary>
    Task<ApprovalDecisionResult> DecideAsync(string engagementId, string approvalId, HitlDecision decision, CancellationToken cancellationToken);

    /// <summary>
    /// The ETag-guarded withdrawal of the request <paramref name="approvalId"/> in partition
    /// <paramref name="engagementId"/> when the run waiting on it is terminated (S13.149, doc 20): from
    /// <see cref="ApprovalRequestStatus.Pending"/> or <see cref="ApprovalRequestStatus.Escalated"/> to
    /// <see cref="ApprovalRequestStatus.Withdrawn"/>, recording <paramref name="withdrawnBy"/> and
    /// <paramref name="withdrawalReason"/>. A request already decided, expired or withdrawn is left as it
    /// stands and comes back <see cref="ApprovalTransitionOutcome.Refused"/> with the stored record; a lost
    /// race is re-read and retried once, so a decision that lands first refuses the withdrawal, and a
    /// withdrawal that lands first refuses the decision. An unknown id is
    /// <see cref="ApprovalTransitionOutcome.NotFound"/>. Both texts must be non-blank.
    /// </summary>
    Task<ApprovalTransitionResult> WithdrawAsync(string engagementId, string approvalId, string withdrawnBy, string withdrawalReason, CancellationToken cancellationToken);

    /// <summary>
    /// The ETag-guarded escalation of the request <paramref name="approvalId"/> in partition
    /// <paramref name="engagementId"/> (doc 06 §7, S13.149): to <see cref="ApprovalRequestStatus.Escalated"/>
    /// and awaiting a decision, from <see cref="ApprovalRequestStatus.Pending"/>, from
    /// <see cref="ApprovalRequestStatus.Escalated"/> (a retried activity), or from a decided request whose
    /// decision is the approver's own <see cref="DecisionKind.Escalate"/>. An approve or reject, an expired
    /// request and a withdrawn one are never overwritten: they come back
    /// <see cref="ApprovalTransitionOutcome.Refused"/> with the stored record. An unknown id is
    /// <see cref="ApprovalTransitionOutcome.NotFound"/>.
    /// </summary>
    Task<ApprovalTransitionResult> EscalateAsync(string engagementId, string approvalId, CancellationToken cancellationToken);

    /// <summary>
    /// The doc 12 §8 recovery-sweep's gate-reraise check: every
    /// <see cref="ApprovalRequest"/> whose <see cref="ApprovalRequest.Status"/> is
    /// <see cref="ApprovalRequestStatus.Decided"/>, across all engagements.
    /// </summary>
    Task<IReadOnlyList<ApprovalRequest>> GetDecidedAsync(CancellationToken cancellationToken);
}
