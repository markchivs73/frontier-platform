using System.Text.Json.Serialization;

namespace Frontier.Platform.Observability;

/// <summary>
/// Aggregated metrics snapshot for an engagement (S3.4): total invocations,
/// overall cache hit rate, cumulative token savings, cost estimates.
/// </summary>
public sealed record EngagementMetricsSnapshot(
    [property: JsonPropertyName("engagement_id"), JsonPropertyOrder(0)]
    string EngagementId,

    [property: JsonPropertyName("invocation_count"), JsonPropertyOrder(1)]
    int InvocationCount,

    [property: JsonPropertyName("total_tokens_sent"), JsonPropertyOrder(2)]
    long TotalTokensSent,

    [property: JsonPropertyName("cached_tokens_total"), JsonPropertyOrder(3)]
    long CachedTokensTotal,

    [property: JsonPropertyName("cache_hit_rate_percent"), JsonPropertyOrder(4)]
    decimal CacheHitRatePercent,

    [property: JsonPropertyName("estimated_cost_saved"), JsonPropertyOrder(5)]
    decimal EstimatedCostSaved,

    [property: JsonPropertyName("snapshot_at_utc"), JsonPropertyOrder(6)]
    DateTime SnapshotAtUtc)
{
    /// <summary>
    /// Overall cache hit rate as a percentage (0–100).
    /// </summary>
    [JsonIgnore]
    public decimal EffectiveCacheHitRate => CacheHitRatePercent;

    /// <summary>
    /// Percentage of total tokens that came from cache.
    /// </summary>
    [JsonIgnore]
    public decimal CachedTokensPercent => TotalTokensSent > 0
        ? Math.Round((CachedTokensTotal * 100m) / TotalTokensSent, 2)
        : 0m;
}
