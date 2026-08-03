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
    /// The doc 12 §8 recovery-sweep's gate-reraise check: every
    /// <see cref="ApprovalRequest"/> whose <see cref="ApprovalRequest.Status"/> is
    /// <see cref="ApprovalRequestStatus.Decided"/>, across all engagements.
    /// </summary>
    Task<IReadOnlyList<ApprovalRequest>> GetDecidedAsync(CancellationToken cancellationToken);
}
