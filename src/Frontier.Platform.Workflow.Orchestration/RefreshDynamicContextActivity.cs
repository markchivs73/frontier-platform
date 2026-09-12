using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The activity half of ADR-CR1's refresh loop (S13.62, doc 04 §8): re-renders the engagement's
/// named dynamic-context components through <see cref="IDynamicContextContentProducer"/> and merges
/// them into the store, returning the epoch the run should pin to next.
/// <para>
/// <b>The decision is the orchestrator's; the work is this activity's.</b> The body decides
/// <em>whether</em> to refresh and when (ADR-PA24: at quiescence only); everything that reads or
/// writes anything happens here, where hard invariant 2 permits it.
/// </para>
/// <para>
/// The merge — rather than an upsert — is what stops a scoped refresh from deleting the keys it did
/// not produce; see <see cref="IEngagementContextStore.MergeDynamicContextAsync"/>.
/// <see cref="TaskActivityContext"/> carries no <see cref="CancellationToken"/>;
/// <see cref="CancellationToken.None"/> is this codebase's established DTF-activity convention.
/// </para>
/// </summary>
[DurableTask(WorkflowActivityNames.RefreshDynamicContextActivity)]
public sealed class RefreshDynamicContextActivity : TaskActivity<RefreshDynamicContextRequest, DynamicContextRefreshResult>
{
    private readonly IDynamicContextContentProducer producer;
    private readonly IDynamicContextRefresher refresher;

    /// <summary>Constructs the activity over the consumer's content producer and the store-side refresher.</summary>
    public RefreshDynamicContextActivity(IDynamicContextContentProducer producer, IDynamicContextRefresher refresher)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(refresher);
        this.producer = producer;
        this.refresher = refresher;
    }

    /// <inheritdoc />
    public override async Task<DynamicContextRefreshResult> RunAsync(TaskActivityContext context, RefreshDynamicContextRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var rendered = await producer.ProduceAsync(input.EngagementId, input.Components, input.Reason, CancellationToken.None);

        return await refresher.RefreshComponentsAsync(input.EngagementId, rendered, input.Reason, CancellationToken.None);
    }
}
