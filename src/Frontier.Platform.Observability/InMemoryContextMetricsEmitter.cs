namespace Frontier.Platform.Observability;

/// <summary>
/// In-memory context metrics emitter (S3.4 PoC): stores tier metrics by execution ID
/// for testing and development. Production implementation would emit to observability
/// backend (OTEL collector, time-series DB, etc.). Thread-safe for concurrent recording.
/// </summary>
internal sealed class InMemoryContextMetricsEmitter : IContextMetricsEmitter
{
    private readonly object _lockObj = new();
    private readonly Dictionary<string, List<ContextTierMetrics>> _metricsByExecutionId = new();
    private readonly Dictionary<string, EngagementMetricsSnapshot> _engagementSnapshots = new();

    /// <summary>
    /// Records tier metrics for an execution, updating engagement aggregates.
    /// </summary>
    public Task EmitTierMetricsAsync(
        string executionId,
        IReadOnlyList<ContextTierMetrics> metrics,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ArgumentNullException.ThrowIfNull(metrics);

        lock (_lockObj)
        {
            // Store execution-level metrics
            if (!_metricsByExecutionId.TryGetValue(executionId, out var list))
            {
                list = new();
                _metricsByExecutionId[executionId] = list;
            }
            list.AddRange(metrics);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns aggregated metrics snapshot for an engagement (if any metrics recorded).
    /// </summary>
    public Task<EngagementMetricsSnapshot?> GetEngagementMetricsAsync(
        string engagementId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engagementId);

        lock (_lockObj)
        {
            if (_engagementSnapshots.TryGetValue(engagementId, out var snapshot))
            {
                return Task.FromResult<EngagementMetricsSnapshot?>(snapshot);
            }
        }

        return Task.FromResult<EngagementMetricsSnapshot?>(null);
    }

    /// <summary>
    /// Update engagement snapshot with new data (called when metrics are recorded).
    /// For PoC: simple aggregation across all recorded metrics for the engagement.
    /// </summary>
    internal void UpdateEngagementSnapshot(string engagementId)
    {
        lock (_lockObj)
        {
            var allMetrics = _metricsByExecutionId.Values.SelectMany(m => m).ToList();

            if (allMetrics.Count == 0)
                return;

            var totalTokensSent = allMetrics.Sum(m => m.EstimatedTokensSent);
            var totalCachedTokens = allMetrics.Where(m => m.CacheHit).Sum(m => m.CacheTokensHit);
            var cacheHitCount = allMetrics.Count(m => m.CacheHit);
            var cacheHitRate = (cacheHitCount * 100m) / allMetrics.Count;

            var snapshot = new EngagementMetricsSnapshot(
                EngagementId: engagementId,
                InvocationCount: allMetrics.Count,
                TotalTokensSent: totalTokensSent,
                CachedTokensTotal: totalCachedTokens,
                CacheHitRatePercent: cacheHitRate,
                EstimatedCostSaved: allMetrics.Sum(m => m.CostDeltaTokens),
                SnapshotAtUtc: DateTime.UtcNow);

            _engagementSnapshots[engagementId] = snapshot;
        }
    }
}
