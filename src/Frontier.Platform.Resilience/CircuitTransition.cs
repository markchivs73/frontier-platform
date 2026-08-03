using System.Diagnostics.CodeAnalysis;
using Polly.CircuitBreaker;

namespace Frontier.Platform.Resilience;

/// <summary>A circuit breaker state change for a <c>(provider, modelId)</c> pair (doc 10 §6), published via <see cref="ICircuitStateProvider.Subscribe"/> to OTEL and alarms.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by CircuitStateProvider tests.")]
public sealed record CircuitTransition
{
    /// <summary>The provider whose breaker transitioned (doc 10 §6 granularity).</summary>
    public required string Provider { get; init; }

    /// <summary>The model whose breaker transitioned (doc 10 §6 granularity).</summary>
    public required string ModelId { get; init; }

    /// <summary>The state before the transition.</summary>
    public required CircuitState From { get; init; }

    /// <summary>The state after the transition.</summary>
    public required CircuitState To { get; init; }

    /// <summary>When the transition occurred.</summary>
    public required DateTime OccurredAtUtc { get; init; }
}
