using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// The DTF history event kinds the audit consolidator (doc 05 §4 step 1, Stage 5) maps
/// onto <see cref="WorkflowEvent"/>. Serializes as a snake_case string,
/// identical to a standard enum (doc 00 §3.5).
/// </summary>
public sealed class WorkflowEventType : SmartEnum<WorkflowEventType>
{
    /// <summary>An activity was scheduled.</summary>
    public static readonly WorkflowEventType TaskScheduled = new("task_scheduled");

    /// <summary>An activity completed successfully.</summary>
    public static readonly WorkflowEventType TaskCompleted = new("task_completed");

    /// <summary>An activity failed.</summary>
    public static readonly WorkflowEventType TaskFailed = new("task_failed");

    /// <summary>An activity was retried after a transient failure (Resilience).</summary>
    public static readonly WorkflowEventType TaskRetried = new("task_retried");

    /// <summary>An external event (e.g. a HITL decision) was raised into the orchestration.</summary>
    public static readonly WorkflowEventType ExternalEventRaised = new("external_event_raised");

    /// <summary>A durable timer fired.</summary>
    public static readonly WorkflowEventType TimerFired = new("timer_fired");

    /// <summary>A sub-orchestration (dispatcher child) was scheduled.</summary>
    public static readonly WorkflowEventType SubOrchestrationScheduled = new("sub_orchestration_scheduled");

    /// <summary>A sub-orchestration (dispatcher child) completed.</summary>
    public static readonly WorkflowEventType SubOrchestrationCompleted = new("sub_orchestration_completed");

    /// <summary>The orchestration instance started.</summary>
    public static readonly WorkflowEventType ExecutionStarted = new("execution_started");

    /// <summary>The orchestration instance completed (success, failure, or cancellation).</summary>
    public static readonly WorkflowEventType ExecutionCompleted = new("execution_completed");

    private WorkflowEventType(string name)
        : base(name)
    {
    }
}
