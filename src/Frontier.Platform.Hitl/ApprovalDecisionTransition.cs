using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Hitl;

/// <summary>
/// The pure half of <see cref="IApprovalStore.DecideAsync"/> (doc 06 §9): whether a request
/// can still take a decision, and the decided record that replaces it. Kept apart from the
/// Cosmos ETag mechanics so the platform's one non-append-only transition is unit-testable
/// (engineering-standards: no private methods; the adapter stays an integration-tested shell).
/// </summary>
internal static class ApprovalDecisionTransition
{
    /// <summary>
    /// A request takes a decision while it is awaiting one: <see cref="ApprovalRequestStatus.Pending"/>,
    /// or <see cref="ApprovalRequestStatus.Escalated"/> — escalation re-routes and reminds, it never
    /// decides (doc 06 §7). <see cref="ApprovalRequestStatus.Decided"/> and
    /// <see cref="ApprovalRequestStatus.Expired"/> are final.
    /// </summary>
    internal static bool CanDecide(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Status == ApprovalRequestStatus.Pending || request.Status == ApprovalRequestStatus.Escalated;
    }

    /// <summary>
    /// The decided record: <see cref="ApprovalRequestStatus.Decided"/> with <paramref name="decision"/>
    /// embedded, everything else untouched. Refuses a decision addressed to another request —
    /// <see cref="HitlDecision.RequestId"/> is how a decision names the gate visit it answers (doc 06 §9),
    /// and a mismatch is a caller defect, never something to store.
    /// </summary>
    internal static ApprovalRequest Apply(ApprovalRequest request, HitlDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);
        if (!string.Equals(decision.RequestId, request.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Decision names request '{decision.RequestId}' but is being applied to '{request.Id}'.", nameof(decision));
        }

        return request with { Status = ApprovalRequestStatus.Decided, Decision = decision };
    }
}
