using Frontier.Platform.Hitl;
using Microsoft.DurableTask;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Marks an approval request <see cref="ApprovalRequestStatus.Escalated"/> when its gate's
/// escalation timer fires before a decision arrives, or its approver escalates (doc 06 §7). The gate
/// keeps waiting on the same <c>WaitForExternalEvent</c> — escalation re-routes and reminds, it never
/// auto-decides.
/// <para>
/// The write is <see cref="IApprovalStore.EscalateAsync"/>'s guarded transition (S13.149), so it never
/// overwrites an approve, a reject, an expired request or a withdrawn one: an escalation still running
/// when its run is terminated cannot re-open the withdrawn request. A refused escalation returns the
/// stored record unchanged; the orchestrator discards the return value either way. Only a request the store does not hold is written whole, as the unguarded
/// upsert always did, so a lost projection document is rebuilt rather than failing the gate.
/// </para>
/// </summary>
[DurableTask(WorkflowActivityNames.EscalateApprovalActivity)]
public sealed class EscalateApprovalActivity(IApprovalStore store) : TaskActivity<ApprovalRequest, ApprovalRequest>
{
    /// <inheritdoc />
    public override async Task<ApprovalRequest> RunAsync(TaskActivityContext context, ApprovalRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var result = await store.EscalateAsync(input.EngagementId, input.Id, CancellationToken.None);
        if (result.Outcome != ApprovalTransitionOutcome.NotFound)
        {
            return result.Request!;
        }

        var escalated = input with { Status = ApprovalRequestStatus.Escalated, Decision = null };
        await store.UpsertAsync(escalated, CancellationToken.None);
        return escalated;
    }
}
