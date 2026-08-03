using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Resilience;

/// <summary>
/// The inner Polly retry layer of a <see cref="ResilienceProfile"/> (doc 10 §4).
/// <see cref="Backoff"/> is <c>"decorrelated-jitter"</c> for every Phase 1 profile
/// (ADR-S3); <see cref="ResiliencePolicyProvider"/> builds exponential backoff with
/// jitter from <see cref="BaseDelayMs"/>/<see cref="MaxDelayMs"/> regardless, but the
/// field is kept so the Cosmos seed shape (<c>tools/dev-setup/cosmos-init.py</c>)
/// matches this catalogue field-for-field.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by Phase1ResilienceProfileCatalogue and ResiliencePolicyProvider tests.")]
public sealed record InnerRetrySpec
{
    /// <summary>Total attempts (including the first), e.g. 5 for <c>llm-default</c>.</summary>
    public required int MaxAttempts { get; init; }

    /// <summary>The backoff strategy name (doc 10 §4); Phase 1 always <c>"decorrelated-jitter"</c>.</summary>
    public required string Backoff { get; init; }

    /// <summary>The base delay before the first retry, in milliseconds.</summary>
    public required int BaseDelayMs { get; init; }

    /// <summary>The maximum delay between retries, in milliseconds.</summary>
    public required int MaxDelayMs { get; init; }
}
