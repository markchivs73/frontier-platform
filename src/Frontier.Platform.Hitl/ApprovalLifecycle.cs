namespace Frontier.Platform.Hitl;

/// <summary>
/// The withdrawal and escalation halves of <see cref="IApprovalStore"/> (S13.149), over the same
/// version-checked documents as <see cref="ApprovalDecider"/>. Kept out of the coverage-excluded
/// <see cref="CosmosApprovalStore"/> so their argument checks and outcomes are measured by the unit gate.
/// </summary>
internal sealed class ApprovalLifecycle(IApprovalDocuments documents)
{
    /// <summary>Withdraws the request because its run was terminated (see <see cref="IApprovalStore.WithdrawAsync"/>).</summary>
    internal Task<ApprovalTransitionResult> WithdrawAsync(string engagementId, string approvalId, string withdrawnBy, string withdrawalReason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(withdrawnBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(withdrawalReason);

        return new ApprovalTransitioner(documents).TransitionAsync(
            engagementId, approvalId, ApprovalStatusTransitions.CanWithdraw, request => ApprovalStatusTransitions.Withdraw(request, withdrawnBy, withdrawalReason), cancellationToken);
    }

    /// <summary>Escalates the request when its gate's timer fires or its approver escalates (see <see cref="IApprovalStore.EscalateAsync"/>).</summary>
    internal Task<ApprovalTransitionResult> EscalateAsync(string engagementId, string approvalId, CancellationToken cancellationToken) =>
        new ApprovalTransitioner(documents).TransitionAsync(
            engagementId, approvalId, ApprovalStatusTransitions.CanEscalate, ApprovalStatusTransitions.Escalate, cancellationToken);
}
