using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Hitl;

/// <summary>
/// The decision loop of <see cref="IApprovalStore.DecideAsync"/> (doc 06 §9): read, check, replace under
/// the read's version, and on a lost race try once more before telling the caller who won. Moved out
/// of the coverage-excluded <see cref="CosmosApprovalStore"/> (S13.193) so the retry and the lost-update
/// handling are measured by the unit gate; the adapter keeps only the SDK calls.
/// </summary>
internal sealed class ApprovalDecider(IApprovalDocuments documents)
{
    /// <summary>
    /// How many times a lost race is retried before the caller is told who won. One retry covers the
    /// only legitimate concurrent writer — <c>EscalateApprovalActivity</c>'s upsert — without looping
    /// behind a decision that has already landed.
    /// </summary>
    internal const int DecideAttempts = 2;

    /// <summary>Applies <paramref name="decision"/> to the request, or reports why it could not (see <see cref="IApprovalStore.DecideAsync"/>).</summary>
    internal async Task<ApprovalDecisionResult> DecideAsync(string engagementId, string approvalId, HitlDecision decision, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engagementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        ArgumentNullException.ThrowIfNull(decision);

        for (var attempt = 0; attempt < DecideAttempts; attempt++)
        {
            if (await TryDecideOnceAsync(engagementId, approvalId, decision, cancellationToken) is { } result)
            {
                return result;
            }
        }

        var winner = await documents.ReadAsync(engagementId, approvalId, cancellationToken);
        return winner is null ? ApprovalDecisionResult.NotFound() : ApprovalDecisionResult.AlreadyDecided(winner.Request);
    }

    /// <summary>
    /// One read-check-replace round: <see langword="null"/> means the replace lost its version
    /// precondition and the caller should read again.
    /// </summary>
    private async Task<ApprovalDecisionResult?> TryDecideOnceAsync(string engagementId, string approvalId, HitlDecision decision, CancellationToken cancellationToken)
    {
        var current = await documents.ReadAsync(engagementId, approvalId, cancellationToken);
        if (current is null)
        {
            return ApprovalDecisionResult.NotFound();
        }

        if (!ApprovalDecisionTransition.CanDecide(current.Request))
        {
            return ApprovalDecisionResult.AlreadyDecided(current.Request);
        }

        var decided = ApprovalDecisionTransition.Apply(current.Request, decision);
        return await documents.TryReplaceAsync(decided, current.Version, cancellationToken) ? ApprovalDecisionResult.Decided(decided) : null;
    }
}
