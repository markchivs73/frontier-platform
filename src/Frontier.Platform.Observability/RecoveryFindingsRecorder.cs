using System.Diagnostics.Metrics;

namespace Frontier.Platform.Observability;

/// <summary>
/// Emits the <c>recovery.findings</c> OTEL counter (C-22) via
/// <see cref="System.Diagnostics.Metrics.Meter"/>, tagged <c>finding_type</c> with the
/// snake_case name of the <see cref="RecoveryFindingType"/>. The doc 11 OTEL pipeline
/// picks up any meter named <see cref="MeterName"/> — no further wiring needed here.
/// </summary>
internal sealed class RecoveryFindingsRecorder : IRecoveryFindingsRecorder, IDisposable
{
    /// <summary>The meter name the doc 11 OTEL pipeline collects recovery metrics under.</summary>
    internal const string MeterName = "Frontier.Recovery";

    /// <summary>The <c>recovery.findings</c> counter name (C-22).</summary>
    internal const string CounterName = "recovery.findings";

    private static readonly Dictionary<RecoveryFindingType, string> TagValues = new()
    {
        [RecoveryFindingType.IsLatestHealed] = "islatest_healed",
        [RecoveryFindingType.GateReraised] = "gate_reraised",
        [RecoveryFindingType.AuditMissing] = "audit_missing",
    };

    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> findingsCounter;

    /// <summary>Creates the <see cref="MeterName"/> meter and its <see cref="CounterName"/> counter.</summary>
    public RecoveryFindingsRecorder() => findingsCounter = meter.CreateCounter<long>(CounterName);

    /// <inheritdoc />
    public void RecordFinding(RecoveryFindingType findingType) =>
        findingsCounter.Add(1, new KeyValuePair<string, object?>("finding_type", TagValues[findingType]));

    /// <inheritdoc />
    public void Dispose() => meter.Dispose();
}
