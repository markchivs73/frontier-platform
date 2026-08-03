using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// Per-tier cache activity for one execution, aggregated by the audit consolidator
/// from <see cref="AuditTelemetryRecord"/>s (doc 05 §3, §6) — the empirical-validation
/// payload for cache-placement hypotheses (doc 00 §1).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record CacheMetrics
{
    /// <summary>Cache activity for the baseline (firm-standards) tier.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("baseline")]
    public required CacheTierMetrics Baseline { get; init; }

    /// <summary>Cache activity for the dynamic (engagement-state) tier.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("dynamic")]
    public required CacheTierMetrics Dynamic { get; init; }

    /// <summary>Cache activity for the real-time tier.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("real_time")]
    public required CacheTierMetrics RealTime { get; init; }
}
