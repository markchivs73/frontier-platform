using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Thin eternal router for dispatcher-mode workflows (doc 00 §4.4, ADR-E8; doc 16 §4).
/// Pure orchestrator body: it receives <c>WorkItem</c> events, spawns a sub-orchestration per item
/// (each running the full graph with normal §4.1–4.3 semantics), and calls <c>ContinueAsNew</c>
/// every <see cref="ContinueAsNewThreshold"/> items to bound DTF history. The dispatcher itself
/// produces no sections and no audit record beyond spawn telemetry; children close normally, with
/// their own snapshots, audit consolidation and purge windows.
/// <para>
/// <b>Two behaviours here will read as bugs and are the design</b> (owner's call, S13.22; ADR-PA25).
/// Spawning is <b>parallel and unbounded</b> — the body races the <c>WorkItem</c> wait against its
/// outstanding children and never throttles, because doc 00 §4.4's whole point is that "a child
/// paused at a human gate never blocks the queue". And <c>ContinueAsNew</c> counts <b>spawns, not
/// completions</b>, firing with every child still in flight: DTF sub-orchestrations are independent
/// instances that survive their parent's generation change, so a rollover mid-flight orphans
/// nothing. Restoring an <c>await</c> inside the loop, a throttle, or a <c>WhenAll</c> at the
/// boundary would each turn the router back into the serial queue this replaced.
/// </para>
/// </summary>
[DurableTask(WorkflowActivityNames.DispatcherOrchestrator)]
public sealed class DispatcherOrchestrator : TaskOrchestrator<GraphOrchestratorInput, GraphOrchestratorResult>
{
    /// <summary>
    /// The ADR-E8 work-item event name — a wire contract shared with the ingest surface that raises
    /// it, so it is a constant rather than a literal in the loop (the ADR-CR1 refresh-name precedent).
    /// </summary>
    public const string WorkItemEventName = "WorkItem";

    /// <summary>
    /// Bounds DTF history growth: the dispatcher calls <c>ContinueAsNew</c> after spawning this
    /// many work items, resetting the instance to a fresh generation (doc 00 §4.4, ADR-E8).
    /// Phase 1: tuned for emulator testing; adjust per ADR-A1 config at S10.1 (deployment).
    /// <c>internal</c> (S13.22) so tests reference the boundary rather than hard-coding it.
    /// </summary>
    internal const int ContinueAsNewThreshold = 100;

    /// <inheritdoc />
    public override async Task<GraphOrchestratorResult> RunAsync(TaskOrchestrationContext context, GraphOrchestratorInput input)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        DispatcherSteps.EnsureDispatcherMode(input.Definition);

        // Both subscriptions are created ONCE and re-armed only after a delivery. DTF writes one
        // history record per subscription, so a wait re-created inside the loop grows history on
        // every pass and consumes buffered events out of order — the hazard documented at
        // GraphOrchestratorSteps.cs:70-77, which this loop inherits wholesale now that it races the
        // WorkItem wait against outstanding children rather than awaiting each child in turn.
        var workItemWait = context.WaitForExternalEvent<WorkItem>(WorkItemEventName);
        var refreshWait = context.WaitForExternalEvent<DynamicContextRefreshRequired>(GraphOrchestratorSteps.DynamicContextRefreshEventName);

        var children = new List<Task<GraphOrchestratorResult>>();
        var spawned = 0;

        while (true)
        {
            await Task.WhenAny([workItemWait, refreshWait, .. children]);

            // Drain and discard. S13.62's fan-out raises the refresh signal at every live instance
            // of an engagement, dispatchers included; a dispatcher has no walk and no epoch to move,
            // so it never acts on one. Without this drain, ContinueAsNew(preserveUnprocessedEvents:
            // true) would carry every such signal into the next generation, and the next, forever —
            // unbounded history growth for the life of an eternal instance, entirely silently.
            if (refreshWait.IsCompleted)
            {
                await refreshWait;
                refreshWait = context.WaitForExternalEvent<DynamicContextRefreshRequired>(GraphOrchestratorSteps.DynamicContextRefreshEventName);
            }

            if (workItemWait.IsCompleted)
            {
                var workItem = await workItemWait;
                workItemWait = context.WaitForExternalEvent<WorkItem>(WorkItemEventName);

                // Deliberately NOT awaited: the child is an independent DTF instance, and awaiting
                // it here is precisely the defect S13.22 removed.
                children.Add(context.CallSubOrchestratorAsync<GraphOrchestratorResult>(
                    new TaskName(WorkflowActivityNames.GraphOrchestrator),
                    DispatcherSteps.BuildChildInput(context, input, workItem)));

                spawned++;

                if (spawned >= ContinueAsNewThreshold)
                {
                    return await RollOverAsync(context, input);
                }
            }

            DispatcherSteps.PruneFinishedChildren(children);
        }
    }

    /// <summary>
    /// The generation boundary (S13.18, ADR-E15 D2): resolves the definition the next generation
    /// should run and continues as new on it, <b>without awaiting the children still in flight</b>.
    /// <para>
    /// The resolve is an activity because it is a store read and a body may not do one (invariant
    /// 2), and it happens here because a running generation stays pinned to the definition its
    /// history recorded (invariant 6) — the version can only move where the generation does.
    /// Buffered work items cross the boundary (<c>preserveUnprocessedEvents</c>, the DTF default),
    /// or an item raised between the Nth spawn and the new generation would be accepted and
    /// silently dropped.
    /// </para>
    /// </summary>
    internal static async Task<GraphOrchestratorResult> RollOverAsync(TaskOrchestrationContext context, GraphOrchestratorInput input)
    {
        var resolved = await context.CallActivityAsync<ResolveDispatcherVersionResult>(
            WorkflowActivityNames.ResolveDispatcherVersionActivity,
            DispatcherSteps.BuildResolveRequest(input));

        context.ContinueAsNew(DispatcherSteps.BuildNextGenerationInput(input, resolved.Definition));

        return new GraphOrchestratorResult
        {
            CompletedSteps = [],
            ArtifactStatuses = new Dictionary<string, ArtifactStatus>(StringComparer.Ordinal),
        };
    }
}
