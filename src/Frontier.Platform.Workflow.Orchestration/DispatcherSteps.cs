using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The deterministic step logic behind <see cref="DispatcherOrchestrator"/> (S13.22), extracted so
/// the router body itself stays a readable loop (engineering-standards: no private methods, small
/// testable internal helpers). Everything here is pure: the only non-deterministic value any of it
/// touches is <see cref="TaskOrchestrationContext.NewGuid"/>, which DTF records and replays.
/// </summary>
internal static class DispatcherSteps
{
    /// <summary>
    /// Throws unless <paramref name="definition"/> declares <see cref="ExecutionMode.Dispatcher"/>.
    /// <para>
    /// A <see cref="ContractViolationException"/>, not an <see cref="InvalidOperationException"/>,
    /// and the distinction is load-bearing rather than cosmetic: hard invariant 7 makes contract
    /// violations <b>permanent</b> failures that are never retried, and that classification is read
    /// off the exception type. A wrong-mode definition retried against the same wall is exactly the
    /// shape the invariant forbids. <c>GraphOrchestratorSteps.EnsureSupported</c> — the mirror-image
    /// guard for the same class of mistake — has always thrown this; one vocabulary now.
    /// </para>
    /// </summary>
    internal static void EnsureDispatcherMode(WorkflowDefinition definition)
    {
        if (definition.Mode != ExecutionMode.Dispatcher)
        {
            throw new ContractViolationException(
                nameof(WorkflowDefinition),
                [$"DispatcherOrchestrator requires '{ExecutionMode.Dispatcher.Name}' definitions; got '{definition.Mode.Name}'."]);
        }
    }

    /// <summary>
    /// Builds the child execution's input for one <paramref name="workItem"/>.
    /// <para>
    /// The child gets <b>its own</b> run id from <see cref="TaskOrchestrationContext.NewGuid"/> —
    /// the recorded, replay-stable GUID that hard invariant 2 permits a body, and the only GUID
    /// source it permits. Children are independent executions with their own snapshots and their
    /// own signed audit records, so sharing the dispatcher's run id would collapse them into one
    /// run's evidence. It inherits the dispatcher's dynamic-context pin, without which two tickets
    /// dispatched from one generation could run against different epochs while their snapshots
    /// claim the same provenance (S13.60/C-42). It carries <see cref="GraphOrchestratorInput.PinModelRoles"/>
    /// forward, so each child pins model-role mappings at its own start; the router pins nothing (ADR-PA29). Attribution follows ADR-E8/S13.19: the work item's
    /// directing human wins, the dispatcher's own initiator is the fallback.
    /// </para>
    /// </summary>
    internal static GraphOrchestratorInput BuildChildInput(TaskOrchestrationContext context, GraphOrchestratorInput input, WorkItem workItem) => new()
    {
        Definition = input.Definition,
        EngagementId = input.EngagementId,
        WorkItemId = workItem.WorkItemId,
        InitiatedBy = workItem.DirectedBy ?? input.InitiatedBy,
        RunId = context.NewGuid().ToString(),
        DynamicContextEpoch = input.DynamicContextEpoch,
        DynamicContextHash = input.DynamicContextHash,
        PinModelRoles = input.PinModelRoles,
    };

    /// <summary>
    /// Builds the next generation's input around <paramref name="resolved"/>, or around the current
    /// definition when nothing newer was resolved.
    /// <para>
    /// A generation change is the <em>same run continuing</em>, so the run's identity and
    /// attribution are preserved deliberately: a new <c>RunId</c> here would fork the audit chain
    /// (ADR-EX1), and a dropped <c>InitiatedBy</c> would break the S13.19 attribution chain at an
    /// arbitrary work-item boundary. Only the definition may move, and only here.
    /// </para>
    /// </summary>
    internal static GraphOrchestratorInput BuildNextGenerationInput(GraphOrchestratorInput input, WorkflowDefinition? resolved) => new()
    {
        Definition = resolved ?? input.Definition,
        EngagementId = input.EngagementId,
        InitiatedBy = input.InitiatedBy,
        RunId = input.RunId,
        DynamicContextEpoch = input.DynamicContextEpoch,
        DynamicContextHash = input.DynamicContextHash,
        PinModelRoles = input.PinModelRoles,
    };

    /// <summary>Projects the dispatcher's pinned input into the S13.18 rollover request.</summary>
    internal static ResolveDispatcherVersionRequest BuildResolveRequest(GraphOrchestratorInput input) => new()
    {
        EngagementId = input.EngagementId,
        WorkflowId = input.Definition.WorkflowId,
        CurrentDefinitionVersion = input.Definition.DefinitionVersion,
    };

    /// <summary>
    /// Drops finished children from the outstanding set, observing any fault so a child that failed
    /// does not surface later as an unobserved task exception.
    /// <para>
    /// Dropping them is what keeps the race in <see cref="DispatcherOrchestrator"/> a wait rather
    /// than a spin: a completed task left in the set makes every subsequent
    /// <see cref="Task.WhenAny(System.Collections.Generic.IEnumerable{Task})"/> return immediately.
    /// Their <em>results</em> are deliberately discarded — a child is an independent execution that
    /// writes its own snapshots and its own audit record, and the dispatcher produces no evidence
    /// beyond spawn telemetry (doc 00 §4.4).
    /// </para>
    /// </summary>
    internal static void PruneFinishedChildren(List<Task<GraphOrchestratorResult>> children)
    {
        children.RemoveAll(child =>
        {
            if (!child.IsCompleted)
            {
                return false;
            }

            _ = child.Exception;
            return true;
        });
    }
}
