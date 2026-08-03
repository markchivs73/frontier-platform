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
}
