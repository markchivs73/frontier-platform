using System.Diagnostics.Metrics;

namespace Frontier.Platform.Observability;

/// <summary>
/// Emits the <c>recovery.findings</c> OTEL counter (C-22) on the shared
/// <see cref="IMetricCatalogue.PlatformMeter"/> (ADR-O1's one meter), tagged with the catalogue's
/// <see cref="DimensionName"/> dimension carrying the snake_case name of the
/// <see cref="RecoveryFindingType"/>.
/// </summary>
internal sealed class RecoveryFindingsRecorder : IRecoveryFindingsRecorder
{
    /// <summary>The <c>recovery.findings</c> counter name (C-22).</summary>
    internal const string CounterName = "recovery.findings";

    /// <summary>The counter's one dimension, as doc 11 §4.3 and the catalogue name it: <c>recovery.findings{kind}</c>.</summary>
    internal const string DimensionName = "kind";

    private static readonly Dictionary<RecoveryFindingType, string> TagValues = new()
    {
        [RecoveryFindingType.IsLatestHealed] = "islatest_healed",
        [RecoveryFindingType.GateReraised] = "gate_reraised",
        [RecoveryFindingType.AuditMissing] = "audit_missing",
        [RecoveryFindingType.FailedExecutionHealed] = "failed_execution_healed",
    };

    private readonly Counter<long> findingsCounter;

    /// <summary>Creates the <see cref="CounterName"/> counter on the catalogue's shared platform meter.</summary>
    /// <param name="catalogue">The metric catalogue whose <see cref="IMetricCatalogue.PlatformMeter"/> owns the counter.</param>
    public RecoveryFindingsRecorder(IMetricCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        findingsCounter = catalogue.PlatformMeter.CreateCounter<long>(CounterName);
    }

    /// <inheritdoc />
    public void RecordFinding(RecoveryFindingType findingType) =>
        findingsCounter.Add(1, new KeyValuePair<string, object?>(DimensionName, TagValues[findingType]));
}
