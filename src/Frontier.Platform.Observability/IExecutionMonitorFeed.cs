namespace Frontier.Platform.Observability;

/// <summary>
/// Live telemetry feed for the running-execution UI (doc 11 §9): streams
/// <see cref="ExecutionMetricEvent"/>s for a given execution as they occur.
/// Phase 1 implementation: <see cref="Phase1ExecutionMonitorFeed"/> returns an empty
/// stream; the real span-processor tap + SignalR hub bridge is Stage 9/doc 19 scope.
/// </summary>
public interface IExecutionMonitorFeed
{
    /// <summary>Streams metric events for <paramref name="executionId"/> until cancelled.</summary>
    IAsyncEnumerable<ExecutionMetricEvent> SubscribeAsync(string executionId, CancellationToken ct);
}
