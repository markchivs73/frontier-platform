using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Hitl;

/// <summary>
/// The pure halves of <see cref="IApprovalStore.WithdrawAsync"/> and <see cref="IApprovalStore.EscalateAsync"/>
/// (S13.149): which stored requests each transition may replace, and the record that replaces them. Kept
/// apart from the Cosmos ETag mechanics, as <see cref="ApprovalDecisionTransition"/> is. Each names the
/// statuses it accepts, so a status added later is refused by construction, and
/// <c>ApprovalStatusTransitionsTests</c> fails until the new status is classified in its table.
/// </summary>
internal static class ApprovalStatusTransitions
{
    /// <summary>
    /// A request is withdrawn while it is still awaiting a decision: exactly the statuses
    /// <see cref="ApprovalDecisionTransition.CanDecide"/> accepts. Decided, expired and withdrawn are final.
    /// </summary>
    internal static bool CanWithdraw(ApprovalRequest request) => ApprovalDecisionTransition.CanDecide(request);

    /// <summary>The withdrawn record: <see cref="ApprovalRequestStatus.Withdrawn"/> with who and why, everything else untouched.</summary>
    internal static ApprovalRequest Withdraw(ApprovalRequest request, string withdrawnBy, string withdrawalReason)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(withdrawnBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(withdrawalReason);
        return request with { Status = ApprovalRequestStatus.Withdrawn, WithdrawnBy = withdrawnBy, WithdrawalReason = withdrawalReason };
    }

    /// <summary>
    /// A request is escalated while it is awaiting a decision (<see cref="ApprovalRequestStatus.Pending"/>,
    /// or <see cref="ApprovalRequestStatus.Escalated"/> again on an activity retry), and when the decision it
    /// holds is the approver's own <see cref="DecisionKind.Escalate"/>: that decision re-routes rather than
    /// decides, and escalating re-opens the request for the real decision, as the unguarded write always did
    /// (doc 06 §7). An approve or reject, an expired request and a withdrawn one are never overwritten.
    /// </summary>
    internal static bool CanEscalate(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Status == ApprovalRequestStatus.Pending
            || request.Status == ApprovalRequestStatus.Escalated
            || (request.Status == ApprovalRequestStatus.Decided && request.Decision?.Kind == DecisionKind.Escalate);
    }

    /// <summary>The escalated record: <see cref="ApprovalRequestStatus.Escalated"/> and awaiting a decision again, so no decision is embedded.</summary>
    internal static ApprovalRequest Escalate(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request with { Status = ApprovalRequestStatus.Escalated, Decision = null };
    }
}
