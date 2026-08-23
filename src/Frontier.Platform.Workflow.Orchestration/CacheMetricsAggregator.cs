
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Aggregates staged <see cref="AuditTelemetryRecord"/>s into <see cref="CacheMetrics"/>
/// (doc 05 §4 step 2, C-15). Per <see cref="CacheTierMetrics"/>'s doc comment: a tier's
/// <see cref="CacheTierMetrics.Reads"/>/<see cref="CacheTierMetrics.Writes"/> counts come
/// from whether that tier's cache breakpoint changed on each invocation, while
/// <see cref="CacheTierMetrics.TokensRead"/> is attributed wholly to
/// <see cref="CacheMetrics.Baseline"/> (the tier whose breakpoint, if any, was hit) —
/// <see cref="CacheMetrics.Dynamic"/>/<see cref="CacheMetrics.RealTime"/> always report 0.
/// </summary>
internal static class CacheMetricsAggregator
{
    /// <summary>Aggregates every staged record for an execution into its <see cref="CacheMetrics"/>.</summary>
    internal static CacheMetrics Aggregate(IReadOnlyList<AuditTelemetryRecord> records) => new()
    {
        Baseline = AggregateTier(records, record => record.BaselineCacheChanged, records.Sum(record => record.CacheReadTokens)),
        Dynamic = AggregateTier(records, record => record.DynamicCacheChanged, tokensRead: 0),
        RealTime = AggregateTier(records, record => record.RealTimeCacheChanged, tokensRead: 0),
    };

    /// <summary>
    /// Builds one tier's <see cref="CacheTierMetrics"/>: <paramref name="changed"/> selects
    /// the tier's per-invocation cache-changed flag — <see langword="true"/> counts as a
    /// write (cache breakpoint refreshed), <see langword="false"/> as a read (cache hit).
    /// </summary>
    internal static CacheTierMetrics AggregateTier(IReadOnlyList<AuditTelemetryRecord> records, Func<AuditTelemetryRecord, bool> changed, long tokensRead)
    {
        var writes = records.Count(changed);
        var reads = records.Count - writes;
        var total = reads + writes;

        return new CacheTierMetrics
        {
            Reads = reads,
            Writes = writes,
            HitRatePercent = total == 0 ? 0m : 100m * reads / total,
            TokensRead = tokensRead,
        };
    }
}
