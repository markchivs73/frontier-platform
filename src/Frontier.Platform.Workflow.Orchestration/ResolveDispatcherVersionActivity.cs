using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The activity half of the dispatcher's version rollover (S13.18, ADR-E15 D2, doc 16 §8): asks
/// the consumer's <see cref="IDispatcherVersionResolver"/> what the next generation should run.
/// <para>
/// <b>Why an activity.</b> Resolving the current published version while honouring doc 16 §8's
/// per-engagement pins is a store read, and hard invariant 2 forbids a body reading any store.
/// The <c>ContinueAsNew</c> boundary is the only correct moment to ask: a definition is immutable
/// once published (invariant 6) and a running generation stays pinned to the one it started with,
/// so the version can only move where the generation does.
/// </para>
/// <para>
/// <see cref="TaskActivityContext"/> carries no <see cref="CancellationToken"/>;
/// <see cref="CancellationToken.None"/> is this codebase's established DTF-activity convention.
/// </para>
/// </summary>
[DurableTask(WorkflowActivityNames.ResolveDispatcherVersionActivity)]
public sealed class ResolveDispatcherVersionActivity : TaskActivity<ResolveDispatcherVersionRequest, ResolveDispatcherVersionResult>
{
    private readonly IDispatcherVersionResolver resolver;

    /// <summary>Constructs the activity over the consumer's version resolver.</summary>
    public ResolveDispatcherVersionActivity(IDispatcherVersionResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        this.resolver = resolver;
    }

    /// <inheritdoc />
    public override async Task<ResolveDispatcherVersionResult> RunAsync(TaskActivityContext context, ResolveDispatcherVersionRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var resolved = await resolver.ResolveAsync(
            input.EngagementId, input.WorkflowId, input.CurrentDefinitionVersion, CancellationToken.None);

        return new ResolveDispatcherVersionResult { Definition = resolved };
    }
}
