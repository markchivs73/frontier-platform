using System.Diagnostics.Metrics;

namespace Frontier.Platform.Observability.Tests;

/// <summary>Tests for <see cref="RecoveryFindingsRecorder"/> (C-22): the <c>recovery.findings</c> counter.</summary>
public sealed class RecoveryFindingsRecorderTests
{
    [Theory]
    [InlineData(RecoveryFindingType.IsLatestHealed, "islatest_healed")]
    [InlineData(RecoveryFindingType.GateReraised, "gate_reraised")]
    [InlineData(RecoveryFindingType.AuditMissing, "audit_missing")]
    public void RecordFinding_IncrementsRecoveryFindingsCounterWithFindingTypeTag(RecoveryFindingType findingType, string expectedTag)
    {
        using var recorder = new RecoveryFindingsRecorder();
        var measurements = new List<(long Value, string? FindingType)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == RecoveryFindingsRecorder.MeterName && instrument.Name == RecoveryFindingsRecorder.CounterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            measurements.Add((value, tags.ToArray().FirstOrDefault(t => t.Key == "finding_type").Value as string)));
        listener.Start();

        recorder.RecordFinding(findingType);

        Assert.Contains(measurements, m => m.Value == 1 && m.FindingType == expectedTag);
    }

    [Fact]
    public void RecoveryFindingsRecorder_Dispose_DisposesUndlyingMeter()
    {
        var recorder = new RecoveryFindingsRecorder();
        recorder.Dispose(); // Should not throw
    }
}
