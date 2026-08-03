using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Resilience;

/// <summary>The outer DTF activity-level retry layer of a <see cref="ResilienceProfile"/> (doc 10 §4, §5: re-runs after inner-loop exhaustion).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by Phase1ResilienceProfileCatalogue and ResiliencePolicyProvider tests.")]
public sealed record OuterRetrySpec
{
    /// <summary>The maximum number of full activity re-runs DTF will attempt.</summary>
    public required int MaxAttempts { get; init; }
}
