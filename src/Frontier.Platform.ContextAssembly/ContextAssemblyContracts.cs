using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Three-tier context model: Baseline (fleet-wide), Dynamic (engagement-specific), RealTime (per-call).
/// All contracts are immutable, versioned, and serialize via the canonical profile.
/// </summary>
public enum ContextTier
{
    /// <summary>Fleet-wide, stable, shared across all engagements (baseline data, pricing templates, etc.).</summary>
    [JsonPropertyName("baseline")]
    Baseline = 0,

    /// <summary>Engagement-specific, moderate refresh cadence (CRM data, project scope, schedule).</summary>
    [JsonPropertyName("dynamic")]
    Dynamic = 1,

    /// <summary>Per-call, never cached, latest signals (real-time pricing ticks, availability changes).</summary>
    [JsonPropertyName("real_time")]
    RealTime = 2
}

/// <summary>
/// Metadata for a ContextPackage: timing, tier boundaries, refresh reason.
/// </summary>
public sealed record ContextPackageMetadata(
    [property: JsonPropertyName("assembled_at_utc"), JsonPropertyOrder(0)]
    DateTime AssembledAtUtc,

    [property: JsonPropertyName("baseline_bytes"), JsonPropertyOrder(1)]
    int BaselineBytes,

    [property: JsonPropertyName("dynamic_bytes"), JsonPropertyOrder(2)]
    int DynamicBytes,

    [property: JsonPropertyName("real_time_bytes"), JsonPropertyOrder(3)]
    int RealTimeBytes,

    [property: JsonPropertyName("refresh_reason"), JsonPropertyOrder(4)]
    string? RefreshReason = null,

    [property: JsonPropertyName("cache_directives"), JsonPropertyOrder(5)]
    IReadOnlyList<ProviderCacheDirective>? CacheDirectives = null)
{
    /// <summary>Validate basic invariants.</summary>
    public void Validate()
    {
        if (BaselineBytes < 0 || DynamicBytes < 0 || RealTimeBytes < 0)
            throw new ContractViolationException("Context tier byte counts cannot be negative.");
    }
}

/// <summary>
/// Cache directive applied to a tier by a specific provider's caching strategy.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Pure data record; tested indirectly through caching strategy tests.")]
public sealed record ProviderCacheDirective(
    [property: JsonPropertyName("tier"), JsonPropertyOrder(0)]
    string Tier,

    [property: JsonPropertyName("provider"), JsonPropertyOrder(1)]
    string Provider,

    [property: JsonPropertyName("strategy"), JsonPropertyOrder(2)]
    string Strategy,

    [property: JsonPropertyName("expires_at_utc"), JsonPropertyOrder(3)]
    DateTime? ExpiresAtUtc = null);

/// <summary>
/// Cache hit/miss observation from a provider response.
/// Extracted post-invocation from provider usage telemetry.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Pure data record; tested indirectly through provider integration tests.")]
public sealed record CacheHitMetrics(
    [property: JsonPropertyName("tier"), JsonPropertyOrder(0)]
    string Tier,

    [property: JsonPropertyName("is_hit"), JsonPropertyOrder(1)]
    bool IsHit,

    [property: JsonPropertyName("bytes_from_cache"), JsonPropertyOrder(2)]
    int BytesFromCache,

    [property: JsonPropertyName("tokens_from_cache"), JsonPropertyOrder(3)]
    int TokensFromCache,

    [property: JsonPropertyName("estimated_cost_savings"), JsonPropertyOrder(4)]
    decimal EstimatedCostSavings);

/// <summary>
/// Metadata about the provider and model for caching strategy resolution.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Pure data record; used as immutable configuration in service tests.")]
public sealed record CachingMetadata(
    string ProviderId,
    string ModelId,
    string? ModelVersion,
    int MaxTokens,
    DateTime AssembledAtUtc);

/// <summary>
/// Configuration for ContextAssembly library. A property record (not positional) with a
/// default for every property — including <see cref="BaselineCatalogueId"/>, which
/// defaults to <see cref="Phase1ContextCatalogue.BaselineCatalogueId"/> — so
/// <c>AddFrontierContextAssembly()</c>'s parameterless <c>ValidateOnStart()</c> can
/// construct a default instance via <c>Activator.CreateInstance&lt;T&gt;()</c>. A
/// positional record's primary constructor, even with default parameter values, is not a
/// parameterless constructor and fails that call at boot.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Configuration options record with only init properties; coverage through DI service collection tests.")]
public sealed record ContextAssemblyOptions
{
    /// <summary>The baseline catalogue id every PoC Gate 3 <c>ContextRequest</c> resolves against.</summary>
    public string BaselineCatalogueId { get; init; } = Phase1ContextCatalogue.BaselineCatalogueId;

    /// <summary>Token budget for the baseline tier.</summary>
    public int BaselineMaxTokens { get; init; } = 1000;

    /// <summary>Token budget for the dynamic tier.</summary>
    public int DynamicMaxTokens { get; init; } = 1000;

    /// <summary>Token budget for the real-time tier.</summary>
    public int RealTimeMaxTokens { get; init; } = 1000;

    /// <summary>How often the dynamic tier is refreshed (ADR-CR1).</summary>
    public TimeSpan DynamicRefreshInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Whether provider-specific caching directives are applied.</summary>
    public bool EnableCaching { get; init; } = true;
}
