using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// Minimal <see cref="TaskOrchestrationContext"/> for exercising <see cref="GraphOrchestratorSteps"/>
/// and <see cref="GraphOrchestrator"/> outside a DTF worker. Activity calls are dispatched to
/// <see cref="ActivityHandlers"/> by <see cref="TaskName.Name"/>. <see cref="CreateTimer"/> always
/// returns an already-completed task, so the SDK's concrete
/// <c>WaitForExternalEvent(name, timeout, ct)</c> helper (which races <see cref="CreateTimer"/>
/// against <see cref="WaitForExternalEvent{T}(string, CancellationToken)"/> via
/// <c>Task.WhenAny</c>) always treats the timer as the immediate winner and cancels the event
/// wait's token. <see cref="WaitForExternalEvent{T}(string, CancellationToken)"/> therefore
/// honours that cancellation for unconfigured events — without it, the awaited event task would
/// never transition and the helper's final <c>await externalEventTask</c> would hang forever.
/// </summary>
internal sealed class FakeTaskOrchestrationContext : TaskOrchestrationContext
{
    /// <summary>Handlers for <see cref="CallActivityAsync{TResult}"/>, keyed by activity name.</summary>
    public Dictionary<string, Func<object?, object>> ActivityHandlers { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Deferrable handlers for <see cref="CallActivityAsync{TResult}"/>, keyed by activity
    /// name and consulted before <see cref="ActivityHandlers"/> (S13.7i/ADR-5): a handler
    /// returning an uncompleted task lets a test hold one branch open while siblings
    /// complete, proving out-of-order completion, the gate barrier, and the failure-drain
    /// policy under the ready-set scheduler.
    /// </summary>
    public Dictionary<string, Func<object?, Task<object>>> AsyncActivityHandlers { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Pre-supplied external-event payloads, keyed by event name. Absent keys never
    /// complete. A <see cref="Queue{T}"/> value is dequeued on each
    /// <see cref="WaitForExternalEvent{T}(string, CancellationToken)"/> call against that
    /// key — for tests where the same event name is awaited more than once (S4.6 gate
    /// re-entry/escalation loops) and each wait must observe a different decision. A
    /// <see cref="DecisionAfterTimeout"/> value lets the first wait against that key
    /// behave like an unconfigured event (cancels via <c>cancellationToken</c>,
    /// simulating the SDK's timeout helper cancelling on a timer win — S4.6 gate
    /// escalation), then replaces itself with its wrapped value so the subsequent
    /// indefinite wait after escalation returns that decision. Any other value is
    /// returned unchanged on every call.
    /// </summary>
    public Dictionary<string, object> ExternalEvents { get; } = new(StringComparer.Ordinal);

    /// <summary>Inputs captured from <see cref="CallSubOrchestratorAsync{TResult}"/> (S13.19: dispatcher spawn assertions).</summary>
    public List<object?> SubOrchestratorInputs { get; } = [];

    /// <summary>Creates a fake context with the given <see cref="InstanceId"/> and <see cref="CurrentUtcDateTime"/>.</summary>
    public FakeTaskOrchestrationContext(string instanceId = "eng-1::wf-chain", DateTime? currentUtcDateTime = null)
    {
        InstanceId = instanceId;
        CurrentUtcDateTime = currentUtcDateTime ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <inheritdoc />
    public override TaskName Name => new("GraphOrchestrator");

    /// <inheritdoc />
    public override string InstanceId { get; }

    /// <inheritdoc />
    public override ParentOrchestrationInstance? Parent => null;

    /// <inheritdoc />
    public override DateTime CurrentUtcDateTime { get; }

    /// <inheritdoc />
    public override bool IsReplaying => false;

    /// <inheritdoc />
    protected override ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

    /// <inheritdoc />
    public override T GetInput<T>() => throw new NotSupportedException("Input is supplied directly to RunAsync in these tests.");

    /// <inheritdoc />
    public override async Task<TResult> CallActivityAsync<TResult>(TaskName name, object? input = null, TaskOptions? options = null)
    {
        if (AsyncActivityHandlers.TryGetValue(name.Name, out var asyncHandler))
        {
            return (TResult)await asyncHandler(input);
        }

        if (!ActivityHandlers.TryGetValue(name.Name, out var handler))
        {
            throw new InvalidOperationException($"No handler registered for activity '{name.Name}'.");
        }

        return (TResult)handler(input);
    }

    /// <inheritdoc />
    public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
    {
        if (ExternalEvents.TryGetValue(eventName, out var value))
        {
            if (value is DecisionAfterTimeout timeoutThen)
            {
                ExternalEvents[eventName] = timeoutThen.Value;
                var pending = new TaskCompletionSource<T>();
                cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
                return pending.Task;
            }

            if (value is PendingExternalEvent pendingEvent)
            {
                return pendingEvent.Next<T>();
            }

            return Task.FromResult(value is Queue<object> queue ? (T)queue.Dequeue() : (T)value);
        }

        var tcs = new TaskCompletionSource<T>();
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return tcs.Task;
    }

    /// <inheritdoc />
    public override void SendEvent(string instanceId, string eventName, object? payload) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetCustomStatus(object? customStatus)
    {
    }

    /// <inheritdoc />
    public override Task<TResult> CallSubOrchestratorAsync<TResult>(TaskName orchestratorName, object? input = null, TaskOptions? options = null)
    {
        SubOrchestratorInputs.Add(input);
        return Task.FromResult<TResult>(default!);
    }

    /// <inheritdoc />
    public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true) => throw new NotSupportedException();

    /// <inheritdoc />
    public override Guid NewGuid() => throw new NotSupportedException();
}

/// <summary>
/// An external event the test raises at a chosen moment (S13.62), for
/// <see cref="FakeTaskOrchestrationContext.ExternalEvents"/>.
/// <para>
/// The plain-value and <see cref="Queue{T}"/> entries above are both fixed before the
/// orchestration starts, which cannot express the S13.62 refresh cases: the refresh subscription
/// is created <em>once, before the walk</em>, and the interesting behaviour is a signal arriving
/// while specific nodes are already in flight. This double models DTF faithfully instead — a wait
/// with no buffered event pends indefinitely; <see cref="Raise"/> delivers to the oldest waiter,
/// or buffers for the next wait if none is outstanding; and each raise is consumed exactly once,
/// so an implementation that re-arms its subscription correctly blocks again rather than
/// re-reading the same event forever.
/// </para>
/// </summary>
internal sealed class PendingExternalEvent
{
    private readonly Queue<object> buffered = new();
    private readonly Queue<TaskCompletionSource<object>> waiters = new();

    /// <summary>How many times a wait against this event has been satisfied — "exactly once per signal".</summary>
    public int DeliveredCount { get; private set; }

    /// <summary>Returns the next delivery: the oldest buffered raise, or a task completed by a future <see cref="Raise"/>.</summary>
    public Task<T> Next<T>()
    {
        if (buffered.Count > 0)
        {
            DeliveredCount++;
            return Task.FromResult((T)buffered.Dequeue());
        }

        var pending = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiters.Enqueue(pending);
        return pending.Task.ContinueWith(completed => (T)completed.Result, TaskScheduler.Default);
    }

    /// <summary>Raises the event with <paramref name="payload"/>, delivering to the oldest outstanding waiter or buffering it.</summary>
    public void Raise(object payload)
    {
        if (waiters.Count > 0)
        {
            DeliveredCount++;
            waiters.Dequeue().SetResult(payload);
            return;
        }

        buffered.Enqueue(payload);
    }
}

/// <summary>
/// Wraps a decision payload for <see cref="FakeTaskOrchestrationContext.ExternalEvents"/>: the
/// first <see cref="FakeTaskOrchestrationContext.WaitForExternalEvent{T}(string, CancellationToken)"/>
/// call against the key cancels (simulating the SDK's timeout helper losing to a fired timer —
/// S4.6 gate escalation), and every call thereafter returns <see cref="Value"/>.
/// </summary>
internal sealed record DecisionAfterTimeout(object Value);
