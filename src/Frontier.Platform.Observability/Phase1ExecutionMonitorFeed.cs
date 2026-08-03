namespace Frontier.Platform.Observability;

/// <summary>
/// Phase 1 stub for <see cref="IExecutionMonitorFeed"/> (doc 11 §9): returns an empty
/// stream. The real implementation (in-process OTEL span-processor tap forwarded to the
/// SignalR hub group <c>engagement:{engagementId}</c>) is Stage 9/doc 19 scope.
/// </summary>
internal sealed class Phase1ExecutionMonitorFeed : IExecutionMonitorFeed
{
    /// <inheritdoc />
    public IAsyncEnumerable<ExecutionMetricEvent> SubscribeAsync(string executionId, CancellationToken ct) =>
        EmptyAsyncEnumerable.Instance;

    /// <summary>Allocation-free empty <see cref="IAsyncEnumerable{T}"/> singleton.</summary>
    internal sealed class EmptyAsyncEnumerable : IAsyncEnumerable<ExecutionMetricEvent>
    {
        internal static readonly EmptyAsyncEnumerable Instance = new();

        /// <inheritdoc />
        public IAsyncEnumerator<ExecutionMetricEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            EmptyAsyncEnumerator.Instance;
    }

    /// <summary>Enumerator that completes immediately.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Current is unreachable — MoveNextAsync always returns false; covered by EmptyAsyncEnumerator tests for MoveNextAsync and DisposeAsync.")]
    internal sealed class EmptyAsyncEnumerator : IAsyncEnumerator<ExecutionMetricEvent>
    {
        internal static readonly EmptyAsyncEnumerator Instance = new();

        /// <inheritdoc />
        public ExecutionMetricEvent Current => default!;

        /// <inheritdoc />
        public ValueTask<bool> MoveNextAsync() => new(false);

        /// <inheritdoc />
        public ValueTask DisposeAsync() => default;
    }
}
