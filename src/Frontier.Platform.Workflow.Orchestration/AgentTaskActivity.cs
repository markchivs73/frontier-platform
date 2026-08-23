using Microsoft.DurableTask;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// <c>InvokeAgentActivity</c> for <see cref="Abstractions.AgentTaskNode"/> (doc 00 §4.3 step
/// 5, S4.2): a thin DTF-activity wrapper delegating to <see cref="IAgentTaskActivityPipeline"/>
/// for the full assemble-context → validate → resolve-model → admit → invoke → validate
/// pipeline. <see cref="TaskActivityContext"/> carries no <see cref="CancellationToken"/>;
/// <see cref="CancellationToken.None"/> is the established convention for DTF activities in
/// this codebase.
/// </summary>
[DurableTask(WorkflowActivityNames.AgentTaskActivity)]
public sealed class AgentTaskActivity : TaskActivity<AgentTaskActivityInput, AgentTaskActivityResult>
{
    private readonly IAgentTaskActivityPipeline pipeline;

    /// <summary>Constructs the activity over its <see cref="IAgentTaskActivityPipeline"/>.</summary>
    public AgentTaskActivity(IAgentTaskActivityPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        this.pipeline = pipeline;
    }

    /// <inheritdoc />
    public override Task<AgentTaskActivityResult> RunAsync(TaskActivityContext context, AgentTaskActivityInput input) =>
        pipeline.RunAsync(input, CancellationToken.None);
}
