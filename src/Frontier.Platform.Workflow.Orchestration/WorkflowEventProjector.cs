using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Projects an <see cref="ExecutionSnapshot"/>'s <see cref="ExecutionSnapshot.CompletedSteps"/>
/// and <see cref="ExecutionSnapshot.Decisions"/> into <see cref="WorkflowEvent"/>s (doc 05 §4
/// step 1, S5.4 QG-5 verdict). <c>Microsoft.DurableTask.Client</c> exposes orchestration
/// metadata only (status, timestamps, serialized I/O) — no granular per-event history — so
/// <see cref="WorkflowEvent"/>s are derived from the execution-snapshot projection the
/// orchestrator already maintains, bookended by execution-started/completed markers.
/// </summary>
internal static class WorkflowEventProjector
{
    /// <summary>Builds the ordered <see cref="WorkflowEvent"/> timeline for <paramref name="snapshot"/>.</summary>
    internal static IReadOnlyList<WorkflowEvent> Project(ExecutionSnapshot snapshot, DateTime startedAtUtc)
    {
        var middle = snapshot.CompletedSteps.Select(ToTaskCompleted)
            .Concat(snapshot.Decisions.Select(ToExternalEventRaised))
            .OrderBy(workflowEvent => workflowEvent.OccurredAtUtc);

        return
        [
            Bookend(WorkflowEventType.ExecutionStarted, startedAtUtc, details: null),
            .. middle,
            Bookend(WorkflowEventType.ExecutionCompleted, snapshot.CheckpointedAtUtc, snapshot.Status.Name),
        ];
    }

    /// <summary>Maps a completed step to a <see cref="WorkflowEventType.TaskCompleted"/> event.</summary>
    internal static WorkflowEvent ToTaskCompleted(StepCompletion step) => new()
    {
        EventType = WorkflowEventType.TaskCompleted,
        NodeId = step.NodeId,
        CorrelationId = step.CorrelationId,
        OccurredAtUtc = step.CompletedAtUtc,
        Details = step.OutputContractType,
    };

    /// <summary>Maps a human gate decision to an <see cref="WorkflowEventType.ExternalEventRaised"/> event.</summary>
    internal static WorkflowEvent ToExternalEventRaised(HitlDecision decision) => new()
    {
        EventType = WorkflowEventType.ExternalEventRaised,
        NodeId = decision.GateId,
        CorrelationId = decision.RequestId,
        OccurredAtUtc = decision.DecidedAtUtc,
        Details = decision.Kind.Name,
    };

    /// <summary>Builds an execution-lifecycle bookend event with no node/correlation id.</summary>
    internal static WorkflowEvent Bookend(WorkflowEventType eventType, DateTime occurredAtUtc, string? details) => new()
    {
        EventType = eventType,
        NodeId = null,
        CorrelationId = null,
        OccurredAtUtc = occurredAtUtc,
        Details = details,
    };
}
