using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Serialization;

/// <summary>
/// The assembled, three-tier prompt context produced by Context Assembly for one
/// <see cref="ContextRequest"/> (doc 04 §3). Ordered least-volatile to most-volatile —
/// baseline → dynamic → real-time — which is simultaneously the correct prompt-cache
/// layout; <see cref="Hints"/> marks the two tier boundaries as cache breakpoints.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record ContextPackage
{
    /// <summary>The slow-changing, fleet-wide shared tier.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("baseline")]
    public required BaselineTier Baseline { get; init; }

    /// <summary>The engagement-specific tier, refreshed on signal-driven cadence.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("dynamic")]
    public required DynamicTier Dynamic { get; init; }

    /// <summary>The per-invocation tier; <c>null</c> when the request did not set <see cref="ContextRequest.RequiresRealTime"/>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("real_time")]
    public RealTimeTier? RealTime { get; init; }

    /// <summary>Cache-breakpoint metadata for the provider message-layout strategy.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("hints")]
    public required CacheHint Hints { get; init; }
}

/// <summary>The baseline (slow-changing, fleet-wide) context tier (doc 04 §3).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record BaselineTier
{
    /// <summary>The governed baseline catalogue release this tier was rendered from, e.g. <c>"2026.06.1"</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("baseline_version")]
    public required string BaselineVersion { get; init; }

    /// <summary>The baseline components included, in catalogue-defined canonical order.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("components")]
    public required IReadOnlyList<string> Components { get; init; }

    /// <summary>The canonical rendering of the selected components.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

/// <summary>The dynamic (engagement-specific) context tier (doc 04 §3).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record DynamicTier
{
    /// <summary>The engagement this tier was assembled for.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("engagement_id")]
    public required EngagementId EngagementId { get; init; }

    /// <summary>Bumped on every signal-driven refresh; a cache-generation marker.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("dynamic_epoch")]
    public required int DynamicEpoch { get; init; }

    /// <summary>The section-state document reference this tier was built from.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("assembled_from_snapshot_ref")]
    public required string AssembledFromSnapshotRef { get; init; }

    /// <summary>The canonical rendering of the engagement-context fields.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

/// <summary>The real-time (per-invocation) context tier (doc 04 §3).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record RealTimeTier
{
    /// <summary>The real-time sources fetched for this invocation.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("fetches")]
    public required IReadOnlyList<RealTimeFetch> Fetches { get; init; }

    /// <summary>The canonical rendering of the fetched real-time data.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

/// <summary>One real-time source fetch performed for a <see cref="RealTimeTier"/> (doc 04 §3).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record RealTimeFetch
{
    /// <summary>The MCP source id that was fetched.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("source_id")]
    public required string SourceId { get; init; }

    /// <summary>UTC timestamp at which the fetch completed.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("fetched_at_utc")]
    public required DateTime FetchedAtUtc { get; init; }

    /// <summary>Whether the fetched data was served from a stale cache rather than a live call.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("is_stale")]
    public required bool IsStale { get; init; }
}

/// <summary>Cache-breakpoint metadata for mapping a <see cref="ContextPackage"/> onto a provider message layout (doc 04 §3).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record CacheHint
{
    /// <summary>Character offset (or block index) marking the end of the baseline tier.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("breakpoint_after_baseline")]
    public required int BreakpointAfterBaseline { get; init; }

    /// <summary>Character offset (or block index) marking the end of the dynamic tier.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("breakpoint_after_dynamic")]
    public required int BreakpointAfterDynamic { get; init; }

    /// <summary>Cache key derived from the baseline version and component set hash.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("baseline_cache_key")]
    public required string BaselineCacheKey { get; init; }

    /// <summary>Cache key derived from the engagement id and dynamic epoch.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("dynamic_cache_key")]
    public required string DynamicCacheKey { get; init; }
}
