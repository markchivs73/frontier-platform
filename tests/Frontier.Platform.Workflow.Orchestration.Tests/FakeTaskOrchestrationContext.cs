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
/// <para>
/// <b>S13.22 additions.</b> <see cref="NewGuid"/> and <see cref="ContinueAsNew"/> threw before the
/// dispatcher cluster, and <see cref="CallSubOrchestratorAsync{TResult}"/> always completed
/// immediately. Between them those three made the dispatcher's defining behaviours unassertable:
/// a child cannot be held open, a generation boundary cannot be inspected, and a child has no
/// identity. They now record and defer instead. Every previously documented behaviour above is
/// unchanged.
/// </para>
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

    /// <summary>Orchestration names captured from <see cref="CallSubOrchestratorAsync{TResult}"/>, positionally aligned with <see cref="SubOrchestratorInputs"/>.</summary>
    public List<string> SubOrchestratorNames { get; } = [];

    /// <summary>
    /// Optional control over each spawned sub-orchestration (S13.22), receiving the spawn's
    /// zero-based ordinal and its input and returning the task the call awaits.
    /// <para>
    /// <b>Why an ordinal rather than a name.</b> A dispatcher spawns every child under the same
    /// orchestration name, so a name-keyed handler cannot hold child 1 open while child 2 runs —
    /// which is the single behaviour ADR-E8 turns on ("a ticket paused at a human gate never
    /// blocks the queue", doc 00 §4.4). Left null, a spawn completes immediately with
    /// <see langword="default"/>, which is what every pre-S13.22 test expects.
    /// </para>
    /// </summary>
    public Func<int, object?, Task<object>>? SubOrchestratorHandler { get; set; }

    /// <summary>
    /// Every <see cref="ContinueAsNew"/> call, in order — input and
    /// <c>preserveUnprocessedEvents</c> both observable.
    /// <para>
    /// It threw before S13.22, which made the throw an exit route: the dispatcher's loop has no
    /// other terminating branch, so its tests asserted <see cref="NotSupportedException"/> and read
    /// the captured child inputs afterwards. An escape hatch cannot express what the generation
    /// boundary actually does — what input the next generation carries, and whether buffered events
    /// survive it — so it records instead, and the body returns normally after calling it.
    /// </para>
    /// </summary>
    public List<ContinueAsNewCall> ContinueAsNewCalls { get; } = [];

    /// <summary>
    /// How many times a subscription has been created per event name — the history shape, not the
    /// delivery count (<see cref="PendingExternalEvent.DeliveredCount"/> is that).
    /// <para>
    /// DTF writes one history record per subscription. A wait re-created on every pass of a loop
    /// therefore grows history without bound and consumes buffered events out of order — the hazard
    /// <c>GraphOrchestratorSteps:70-77</c> documents for the refresh wait, and the same hazard for
    /// the dispatcher's <c>WorkItem</c> wait once the loop races that wait against outstanding
    /// children. A correct body holds exactly one outstanding subscription per event name, so the
    /// count here is deliveries + 1.
    /// </para>
    /// </summary>
    public Dictionary<string, int> ExternalEventSubscriptions { get; } = new(StringComparer.Ordinal);

    /// <summary>How many times <see cref="NewGuid"/> has been called.</summary>
    public int NewGuidCallCount { get; private set; }

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
    public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
    {
        TimerFireAts.Add(fireAt);
        return Task.CompletedTask;
    }

    /// <summary>Every <see cref="CreateTimer"/> fire time, in order — the timer actions a real replay would match against history (S13.99).</summary>
    public List<DateTime> TimerFireAts { get; } = [];

    /// <inheritdoc />
    public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
    {
        ExternalEventSubscriptions[eventName] = ExternalEventSubscriptions.GetValueOrDefault(eventName) + 1;

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
    public override async Task<TResult> CallSubOrchestratorAsync<TResult>(TaskName orchestratorName, object? input = null, TaskOptions? options = null)
    {
        var ordinal = SubOrchestratorInputs.Count;
        SubOrchestratorInputs.Add(input);
        SubOrchestratorNames.Add(orchestratorName.Name);

        if (SubOrchestratorHandler is null)
        {
            return default!;
        }

        return (TResult)await SubOrchestratorHandler(ordinal, input);
    }

    /// <inheritdoc />
    public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true) =>
        ContinueAsNewCalls.Add(new ContinueAsNewCall(newInput, preserveUnprocessedEvents));

    /// <summary>
    /// A replay-stable GUID sequence: the <em>n</em>th call of a run always returns the same value,
    /// so two runs of one script produce one identity set (hard invariant 2 —
    /// <c>context.NewGuid()</c> is the only GUID source a body may use, precisely because DTF
    /// records and replays it).
    /// <para>
    /// It threw before S13.22, which is why nothing could assert on child identity at all.
    /// </para>
    /// </summary>
    public override Guid NewGuid()
    {
        NewGuidCallCount++;
        return new Guid(NewGuidCallCount, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);
    }
}

/// <summary>One recorded <see cref="FakeTaskOrchestrationContext.ContinueAsNew"/> call (S13.22).</summary>
/// <param name="Input">The input the next generation would start from.</param>
/// <param name="PreserveUnprocessedEvents">Whether buffered events cross the generation boundary.</param>
internal sealed record ContinueAsNewCall(object? Input, bool PreserveUnprocessedEvents);

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
    private readonly Queue<Action<object>> waiters = new();

    /// <summary>How many times a wait against this event has been satisfied — "exactly once per signal".</summary>
    public int DeliveredCount { get; private set; }

    /// <summary>
    /// Returns the next delivery: the oldest buffered raise, or a task completed by a future
    /// <see cref="Raise"/>.
    /// <para>
    /// <b>Delivery is synchronous</b> (S13.62). The waiter is a plain
    /// <see cref="TaskCompletionSource{T}"/> completed directly by <see cref="Raise"/>, so its
    /// continuation runs inline on the raising thread — the same way <c>CompleteNode</c>'s node
    /// task already behaves, and the way a real DTF orchestrator body behaves: single-threaded,
    /// no thread-pool hop. The earlier form asynchronously hopped <em>twice</em>
    /// (<c>RunContinuationsAsynchronously</c> plus a <c>ContinueWith</c> on
    /// <see cref="TaskScheduler.Default"/>) while node completion stayed inline, so the walk had
    /// two asymmetric resumption paths racing: a signal raised before a node completed was
    /// observed by the walk only sometimes, and a determinism test whose harness is itself
    /// non-deterministic proves nothing either way.
    /// </para>
    /// </summary>
    public Task<T> Next<T>()
    {
        if (buffered.Count > 0)
        {
            DeliveredCount++;
            return Task.FromResult((T)buffered.Dequeue());
        }

        var pending = new TaskCompletionSource<T>();
        waiters.Enqueue(payload => pending.SetResult((T)payload));
        return pending.Task;
    }

    /// <summary>Raises the event with <paramref name="payload"/>, delivering to the oldest outstanding waiter or buffering it.</summary>
    public void Raise(object payload)
    {
        if (waiters.Count > 0)
        {
            DeliveredCount++;
            waiters.Dequeue()(payload);
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
