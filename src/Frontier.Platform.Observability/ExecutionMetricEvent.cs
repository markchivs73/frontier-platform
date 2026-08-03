using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Observability;

/// <summary>The kind of event emitted by <see cref="IExecutionMonitorFeed"/> (doc 11 §9).</summary>
public enum ExecutionMetricEventKind
{
    /// <summary>A graph node began executing.</summary>
    NodeStarted,

    /// <summary>A graph node completed (successfully or with a recoverable error).</summary>
    NodeCompleted,

    /// <summary>Tokens were consumed by an agent invocation (includes per-tier cache verdict).</summary>
    TokensConsumed,

    /// <summary>A retry was issued by the resilience pipeline.</summary>
    RetryIssued,

    /// <summary>A HITL gate was opened and is awaiting a decision.</summary>
    GateOpened,
}

/// <summary>
/// One live telemetry event from a running execution (doc 11 §9), streamed via
/// <see cref="IExecutionMonitorFeed"/> to the running-execution UI over SignalR.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record ExecutionMetricEvent(
    string NodeId,
    ExecutionMetricEventKind Kind,
    DateTimeOffset TimestampUtc,
    long? DurationMs,
    int? TokensConsumed,
    bool? CacheHit);
