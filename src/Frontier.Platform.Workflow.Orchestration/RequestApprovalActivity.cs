using Frontier.Platform.Hitl;
using Microsoft.DurableTask;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Opens a <see cref="HumanGateNode"/>'s approval request (doc 06 §4): builds the
/// <see cref="ApprovalRequestStatus.Pending"/> <see cref="ApprovalRequest"/> via
/// <see cref="ApprovalRequestFactory.Open"/> and persists it via
/// <see cref="IApprovalStore"/>. Called by <c>GraphOrchestratorSteps.RunGateAsync</c>
/// (Orchestration) before <c>WaitForExternalEvent("Gate:{gateId}")</c>.
/// </summary>
[DurableTask(WorkflowActivityNames.RequestApprovalActivity)]
public sealed class RequestApprovalActivity(IApprovalStore store) : TaskActivity<GateOpenRequest, ApprovalRequest>
{
    /// <inheritdoc />
    public override async Task<ApprovalRequest> RunAsync(TaskActivityContext context, GateOpenRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var request = ApprovalRequestFactory.Open(input);
        await store.UpsertAsync(request, CancellationToken.None);

        return request;
    }
}
