namespace Frontier.Platform.Hitl;

/// <summary>
/// The guarded write behind every non-append-only transition of an <see cref="ApprovalRequest"/> (doc 06 §9,
/// S13.149): read, check, replace under the read's version, and on a lost race try once more from a fresh
/// read before reporting what is stored. <see cref="ApprovalDecider"/>, <see cref="IApprovalStore.WithdrawAsync"/>
/// and <see cref="IApprovalStore.EscalateAsync"/> all run over it, so a decision, a withdrawal and an
/// escalation racing each other cannot overwrite one another: whichever lands first, the others re-read it and
/// are refused if it no longer allows them.
/// </summary>
internal sealed class ApprovalTransitioner(IApprovalDocuments documents)
{
    /// <summary>
    /// How many times a lost race is retried before the caller is told what is stored. One retry covers a
    /// single concurrent writer without looping behind a transition that has already landed.
    /// </summary>
    internal const int Attempts = 2;

    /// <summary>
    /// Replaces the request with <paramref name="apply"/>'s record when <paramref name="canApply"/> accepts the
    /// stored one; otherwise reports it refused, or not found.
    /// </summary>
    internal async Task<ApprovalTransitionResult> TransitionAsync(string engagementId, string approvalId, Func<ApprovalRequest, bool> canApply, Func<ApprovalRequest, ApprovalRequest> apply, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engagementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (await TryOnceAsync(engagementId, approvalId, canApply, apply, cancellationToken) is { } result)
            {
                return result;
            }
        }

        var winner = await documents.ReadAsync(engagementId, approvalId, cancellationToken);
        return winner is null ? ApprovalTransitionResult.NotFound() : ApprovalTransitionResult.Refused(winner.Request);
    }

    /// <summary>One read-check-replace round: <see langword="null"/> means the replace lost its version precondition.</summary>
    private async Task<ApprovalTransitionResult?> TryOnceAsync(string engagementId, string approvalId, Func<ApprovalRequest, bool> canApply, Func<ApprovalRequest, ApprovalRequest> apply, CancellationToken cancellationToken)
    {
        var current = await documents.ReadAsync(engagementId, approvalId, cancellationToken);
        if (current is null)
        {
            return ApprovalTransitionResult.NotFound();
        }

        if (!canApply(current.Request))
        {
            return ApprovalTransitionResult.Refused(current.Request);
        }

        var next = apply(current.Request);
        return await documents.TryReplaceAsync(next, current.Version, cancellationToken) ? ApprovalTransitionResult.Applied(next) : null;
    }
}
