using System.Text.Json.Serialization;

namespace Frontier.Platform.Observability;

/// <summary>
/// Per-tier metrics for context assembly (S3.4): cache hit rate, token usage,
/// and byte counts across the three tiers (Baseline, Dynamic, Real-Time).
/// Emitted post-invocation via observability/telemetry system.
/// </summary>
public sealed record ContextTierMetrics(
    [property: JsonPropertyName("tier"), JsonPropertyOrder(0)]
    string Tier,

    [property: JsonPropertyName("bytes_sent"), JsonPropertyOrder(1)]
    int BytesSent,

    [property: JsonPropertyName("estimated_tokens_sent"), JsonPropertyOrder(2)]
    int EstimatedTokensSent,

    [property: JsonPropertyName("cache_hit"), JsonPropertyOrder(3)]
    bool CacheHit,

    [property: JsonPropertyName("cache_bytes_hit"), JsonPropertyOrder(4)]
    int CacheBytesHit,

    [property: JsonPropertyName("cache_tokens_hit"), JsonPropertyOrder(5)]
    int CacheTokensHit,

    [property: JsonPropertyName("cost_delta_tokens"), JsonPropertyOrder(6)]
    decimal CostDeltaTokens)
{
    /// <summary>
    /// Hit rate as a percentage (0–100). CacheHit determines whether this tier
    /// was served from cache or freshly computed/fetched.
    /// </summary>
    [JsonIgnore]
    public decimal HitRatePercent => CacheHit ? 100m : 0m;

    /// <summary>
    /// Token savings from cache (tokens that would have been charged without caching).
    /// Positive value represents savings; 0 if not cached.
    /// </summary>
    [JsonIgnore]
    public int TokenSavings => CacheHit ? CacheTokensHit : 0;
}
