using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Resilience;

/// <summary>
/// A named resilience policy (doc 10 §4): policy as data, versioned in the
/// <c>resilience-profiles</c> Cosmos container (PK <c>/profileId</c>, append-only
/// versions + current pointer, same shape as <c>model-role-config</c>). Phase 1 ships
/// <see cref="Phase1ResilienceProfileCatalogue"/> as the compiled-in source of truth;
/// <see cref="ResiliencePolicyProvider"/> builds both loops (doc 10 §5) from it.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by Phase1ResilienceProfileCatalogue and ResiliencePolicyProvider tests.")]
public sealed record ResilienceProfile
{
    /// <summary>The profile's name, e.g. <c>"llm-default"</c> (doc 10 §4) — the partition key in <c>resilience-profiles</c>.</summary>
    public required string ProfileId { get; init; }

    /// <summary>The profile's version number (append-only versioning, doc 02 §5 pattern).</summary>
    public required int Version { get; init; }

    /// <summary>The inner Polly retry layer.</summary>
    public required InnerRetrySpec InnerRetry { get; init; }

    /// <summary>The per-attempt provider timeout, in milliseconds (doc 10 §7).</summary>
    public required int TimeoutMs { get; init; }

    /// <summary>The circuit breaker layer.</summary>
    public required CircuitBreakerSpec CircuitBreaker { get; init; }

    /// <summary>The bulkhead (concurrency limiter) layer.</summary>
    public required BulkheadSpec Bulkhead { get; init; }

    /// <summary>The outer DTF activity-level retry layer.</summary>
    public required OuterRetrySpec OuterRetry { get; init; }
}
