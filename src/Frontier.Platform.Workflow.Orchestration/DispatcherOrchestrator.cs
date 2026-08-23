using Microsoft.DurableTask;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Thin eternal router for dispatcher-mode workflows (doc 00 §4.4, ADR-E8; doc 16 §4).
/// Pure orchestrator body: receives <c>WorkItem</c> events, spawns sub-orchestrations
/// (each running the full graph with normal §4.1–4.3 semantics), and calls
/// <c>ContinueAsNew</c> every N items to bound DTF history. Dispatcher itself produces
/// no sections and no audit record beyond spawn telemetry. Children run independently
/// (affinity holds at dispatcher level for spawning), process in parallel, and close
/// normally (snapshots, audit consolidation, purge windows apply per child).
/// </summary>
[DurableTask(WorkflowActivityNames.DispatcherOrchestrator)]
public sealed class DispatcherOrchestrator : TaskOrchestrator<GraphOrchestratorInput, GraphOrchestratorResult>
{
    /// <summary>
    /// Bounds DTF history growth: dispatcher calls ContinueAsNew after processing this many
    /// work items, resetting the instance to a fresh generation (doc 00 §4.4, ADR-E8).
    /// Phase 1: tuned for emulator testing; adjust per ADR-A1 config at S10.1 (deployment).
    /// </summary>
    private const int ContinueAsNewThreshold = 100;

    /// <inheritdoc />
    public override async Task<GraphOrchestratorResult> RunAsync(TaskOrchestrationContext context, GraphOrchestratorInput input)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (input.Definition.Mode != ExecutionMode.Dispatcher)
        {
            throw new InvalidOperationException(
                $"DispatcherOrchestrator requires ExecutionMode.Dispatcher, but definition has {input.Definition.Mode.Name}");
        }

        var itemsProcessed = 0;

        while (true)
        {
            var workItem = await context.WaitForExternalEvent<WorkItem>("WorkItem");

            var childInput = new GraphOrchestratorInput
            {
                Definition = input.Definition,
                EngagementId = input.EngagementId,
                WorkItemId = workItem.WorkItemId,
                // ADR-E8 (S13.19): per-item attribution survives the spawn — the work item's
                // directing human wins; the dispatcher's own initiator is the fallback.
                InitiatedBy = workItem.DirectedBy ?? input.InitiatedBy,
            };

            await context.CallSubOrchestratorAsync<GraphOrchestratorResult>(
                new TaskName(WorkflowActivityNames.GraphOrchestrator),
                childInput);

            itemsProcessed++;

            if (itemsProcessed >= ContinueAsNewThreshold)
            {
                context.ContinueAsNew(input);
                return new GraphOrchestratorResult { CompletedSteps = [], ArtifactStatuses = new Dictionary<string, ArtifactStatus>() };
            }
        }
    }
}
