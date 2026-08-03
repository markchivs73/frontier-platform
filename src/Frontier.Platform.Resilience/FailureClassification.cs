using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Resilience;

/// <summary>
/// The result of <see cref="IFailureClassifier.Classify"/> (doc 10 §2, §3): the
/// taxonomy bucket, an optional provider-stated retry delay, and a stable reason code
/// for telemetry (<c>resilience.retry</c>, doc 10 §8) and operator triage.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by FailureClassifier tests.")]
public sealed record FailureClassification
{
    /// <summary>The taxonomy bucket (doc 10 §3).</summary>
    public required FailureClass Class { get; init; }

    /// <summary>The interval the provider asked callers to wait before retrying, if known (doc 10 §3 "never guess shorter").</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>A stable, lower-snake-case reason code from doc 10 §3's mapping table (e.g. <c>"contract_violation"</c>, <c>"unclassified"</c>).</summary>
    public required string ReasonCode { get; init; }
}
