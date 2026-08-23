using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// Minimal <see cref="TaskActivityContext"/> for exercising activity shells outside a DTF
/// worker. Defaults to <see cref="WorkflowActivityNames.AgentTaskActivity"/>; the approval
/// activities (relocated here at S11.3) use the named factory helpers.
/// </summary>
internal sealed class FakeTaskActivityContext(string activityName = WorkflowActivityNames.AgentTaskActivity) : TaskActivityContext
{
    /// <inheritdoc />
    public override TaskName Name => new(activityName);

    /// <inheritdoc />
    public override string InstanceId => "eng-1::wf-chain";

    /// <summary>A context named after <see cref="WorkflowActivityNames.RequestApprovalActivity"/>.</summary>
    internal static FakeTaskActivityContext ForRequestApproval() => new(WorkflowActivityNames.RequestApprovalActivity);

    /// <summary>A context named after <see cref="WorkflowActivityNames.EscalateApprovalActivity"/>.</summary>
    internal static FakeTaskActivityContext ForEscalateApproval() => new(WorkflowActivityNames.EscalateApprovalActivity);
}
