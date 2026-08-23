using Frontier.Platform.Hitl;
using Microsoft.DurableTask;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Marks a pending approval request <see cref="ApprovalRequestStatus.Escalated"/> when
/// its gate's escalation timer fires before a decision arrives (doc 06 §7). The gate
/// keeps waiting on the same <c>WaitForExternalEvent</c> — escalation re-routes and
/// reminds, it never auto-decides.
/// </summary>
[DurableTask(WorkflowActivityNames.EscalateApprovalActivity)]
public sealed class EscalateApprovalActivity(IApprovalStore store) : TaskActivity<ApprovalRequest, ApprovalRequest>
{
    /// <inheritdoc />
    public override async Task<ApprovalRequest> RunAsync(TaskActivityContext context, ApprovalRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var escalated = input with { Status = ApprovalRequestStatus.Escalated };
        await store.UpsertAsync(escalated, CancellationToken.None);

        return escalated;
    }
}
