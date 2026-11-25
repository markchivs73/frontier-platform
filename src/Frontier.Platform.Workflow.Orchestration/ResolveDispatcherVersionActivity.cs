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
    private readonly IDispatcherGenerationSettingsResolver? settings;

    /// <summary>Constructs the activity over the consumer's version resolver only; every generation carries its settings forward.</summary>
    [Obsolete("Register IDispatcherGenerationSettingsResolver so the container uses the two-port constructor and a running dispatcher re-reads its governed settings at each rollover (consumer S13.146, v0.6x). Removed at the next phase boundary.")]
    public ResolveDispatcherVersionActivity(IDispatcherVersionResolver resolver)
        : this(resolver, null)
    {
    }

    /// <summary>
    /// Constructs the activity over the consumer's version resolver and its generation-settings resolver (consumer
    /// S13.146): each rollover then also reads the current contract budgets and rollover threshold, with their versions.
    /// </summary>
    public ResolveDispatcherVersionActivity(IDispatcherVersionResolver resolver, IDispatcherGenerationSettingsResolver? settings)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        this.resolver = resolver;
        this.settings = settings;
    }

    /// <inheritdoc />
    public override async Task<ResolveDispatcherVersionResult> RunAsync(TaskActivityContext context, ResolveDispatcherVersionRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var resolved = await resolver.ResolveAsync(
            input.EngagementId, input.WorkflowId, input.CurrentDefinitionVersion, CancellationToken.None);
        var current = settings is null ? new DispatcherGenerationSettings() : await settings.ResolveAsync(input.EngagementId, input.WorkflowId, CancellationToken.None);

        return new ResolveDispatcherVersionResult
        {
            Definition = resolved,
            ContractBudgets = current.ContractBudgets,
            ContractBudgetsVersion = current.ContractBudgetsVersion,
            DispatcherRolloverThreshold = current.DispatcherRolloverThreshold,
            DispatcherSettingsVersion = current.DispatcherSettingsVersion,
        };
    }
}
