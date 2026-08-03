using System.Diagnostics.Metrics;

namespace Frontier.Platform.Observability;

/// <summary>
/// The authoritative Phase 1 metric catalogue (doc 11 §4): every platform metric, its
/// instrument type, unit, and the closed dimension set (ADR-O1). All recorders share
/// <see cref="PlatformMeter"/> so every metric flows through the same OTEL pipeline.
/// </summary>
public interface IMetricCatalogue
{
    /// <summary>All Phase 1 metric definitions.</summary>
    IReadOnlyList<MetricDefinition> All { get; }

    /// <summary>
    /// The single <see cref="System.Diagnostics.Metrics.Meter"/> for all platform metric
    /// instruments — recorders must use this meter, never create their own (ADR-O1).
    /// </summary>
    Meter PlatformMeter { get; }
}
