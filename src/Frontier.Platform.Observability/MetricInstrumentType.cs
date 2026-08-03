namespace Frontier.Platform.Observability;

/// <summary>The OTEL instrument kind for a metric (doc 11 §4).</summary>
public enum MetricInstrumentType
{
    /// <summary>Monotonically increasing total (e.g., invocation counts).</summary>
    Counter,

    /// <summary>Point-in-time sampled value (e.g., active executions).</summary>
    ObservableGauge,

    /// <summary>Distribution of values (e.g., latency).</summary>
    Histogram,
}
