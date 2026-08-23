using Microsoft.DurableTask;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// <c>InvokeMcpToolActivity</c> for <see cref="Abstractions.McpToolNode"/> (doc 00 §3.2,
/// S13.7c): a thin DTF-activity wrapper delegating to
/// <see cref="IMcpToolInvocationPipeline"/> for the resolve → map arguments → call →
/// canonicalize flow. <see cref="TaskActivityContext"/> carries no
/// <see cref="CancellationToken"/>; <see cref="CancellationToken.None"/> is the
/// established convention for DTF activities in this codebase.
/// </summary>
[DurableTask(WorkflowActivityNames.InvokeMcpToolActivity)]
public sealed class InvokeMcpToolActivity : TaskActivity<McpToolActivityInput, McpToolActivityResult>
{
    private readonly IMcpToolInvocationPipeline pipeline;

    /// <summary>Constructs the activity over its <see cref="IMcpToolInvocationPipeline"/>.</summary>
    public InvokeMcpToolActivity(IMcpToolInvocationPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        this.pipeline = pipeline;
    }

    /// <inheritdoc />
    public override Task<McpToolActivityResult> RunAsync(TaskActivityContext context, McpToolActivityInput input) =>
        pipeline.RunAsync(input, CancellationToken.None);
}
