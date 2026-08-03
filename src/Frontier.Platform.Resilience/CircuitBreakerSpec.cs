using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Resilience;

/// <summary>The circuit breaker layer of a <see cref="ResilienceProfile"/> (doc 10 §4, §6: granularity <c>(provider, modelId)</c>).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by Phase1ResilienceProfileCatalogue and ResiliencePolicyProvider tests.")]
public sealed record CircuitBreakerSpec
{
    /// <summary>The fraction of failures within the sampling window that opens the circuit.</summary>
    public required double FailureRatio { get; init; }

    /// <summary>The minimum number of executions in the sampling window before the breaker can act.</summary>
    public required int MinThroughput { get; init; }

    /// <summary>The rolling window over which the failure ratio is measured, in seconds.</summary>
    public required int SamplingWindowSeconds { get; init; }

    /// <summary>How long the circuit stays open before allowing a half-open probe, in seconds.</summary>
    public required int BreakDurationSeconds { get; init; }
}
