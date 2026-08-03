using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Resilience;

/// <summary>The bulkhead (concurrency limiter) layer of a <see cref="ResilienceProfile"/> (doc 10 §4, §6: granularity <c>provider</c>).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by Phase1ResilienceProfileCatalogue and ResiliencePolicyProvider tests.")]
public sealed record BulkheadSpec
{
    /// <summary>The dimension the limit applies across (doc 10 §6, e.g. <c>"provider"</c>).</summary>
    public required string Scope { get; init; }

    /// <summary>The maximum number of concurrent in-flight calls.</summary>
    public required int MaxConcurrent { get; init; }

    /// <summary>The maximum number of calls allowed to queue once <see cref="MaxConcurrent"/> is reached.</summary>
    public required int MaxQueue { get; init; }
}
