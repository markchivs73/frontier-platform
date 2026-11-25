namespace Frontier.Platform.Hitl;

/// <summary>
/// Queries for <see cref="ApprovalRequest"/> documents from the <c>approvals</c> container
/// (doc 06 §9). Read-only governance surface for listing and filtering approval requests.
/// </summary>
public interface IApprovalQueryService
{
    /// <summary>Point-reads an approval request by its ID.</summary>
    Task<ApprovalRequest?> GetAsync(string approvalId, CancellationToken cancellationToken);

    /// <summary>Lists all approval requests for a given engagement, ordered by creation time descending.</summary>
    Task<IReadOnlyList<ApprovalRequest>> GetByEngagementAsync(string engagementId, CancellationToken cancellationToken);

    /// <summary>Lists all pending approval requests across all engagements (doc 06 §9, governance query).</summary>
    Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken cancellationToken);

    /// <summary>Lists all escalated approval requests across all engagements (doc 06 §7, escalation tracking).</summary>
    Task<IReadOnlyList<ApprovalRequest>> GetEscalatedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The approval requests <paramref name="approverId"/> decided at or after <paramref name="sinceUtc"/> that
    /// are still <see cref="ApprovalRequestStatus.Decided"/>, across all engagements, newest decision first
    /// (S13.312, doc 06 §9). Bounded by the decider and the window, unlike
    /// <see cref="IApprovalStore.GetDecidedAsync"/>. The result is a superset at the sub-millisecond edge of the
    /// window, and requests decided at the same millisecond come in no defined order, so a caller that needs an
    /// exact window or a stable order applies its own.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="approverId"/> is blank, or <paramref name="sinceUtc"/> is not UTC.</exception>
    Task<IReadOnlyList<ApprovalRequest>> GetDecidedByAsync(string approverId, DateTime sinceUtc, CancellationToken cancellationToken);
}
