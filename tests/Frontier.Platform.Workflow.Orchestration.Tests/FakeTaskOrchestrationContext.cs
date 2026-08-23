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
/// Wraps a decision payload for <see cref="FakeTaskOrchestrationContext.ExternalEvents"/>: the
/// first <see cref="FakeTaskOrchestrationContext.WaitForExternalEvent{T}(string, CancellationToken)"/>
/// call against the key cancels (simulating the SDK's timeout helper losing to a fired timer —
/// S4.6 gate escalation), and every call thereafter returns <see cref="Value"/>.
/// </summary>
internal sealed record DecisionAfterTimeout(object Value);
