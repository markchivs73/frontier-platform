using System.Diagnostics.Metrics;

namespace Frontier.Platform.Observability.Tests;

/// <summary>Tests for <see cref="RecoveryFindingsRecorder"/> (C-22): the <c>recovery.findings</c> counter.</summary>
public sealed class RecoveryFindingsRecorderTests
{
    /// <summary>Every <see cref="RecoveryFindingType"/> value, so a new member without a tag fails here, not in the sweep.</summary>
    public static TheoryData<RecoveryFindingType> AllFindingTypes() => new(Enum.GetValues<RecoveryFindingType>());

    [Theory]
    [InlineData(RecoveryFindingType.IsLatestHealed, "islatest_healed")]
    [InlineData(RecoveryFindingType.GateReraised, "gate_reraised")]
    [InlineData(RecoveryFindingType.AuditMissing, "audit_missing")]
    [InlineData(RecoveryFindingType.FailedExecutionHealed, "failed_execution_healed")]
    public void RecordFinding_IncrementsRecoveryFindingsCounterWithKindTag(RecoveryFindingType findingType, string expectedTag)
    {
        using var catalogue = new Phase1MetricCatalogue();
        var recorder = new RecoveryFindingsRecorder(catalogue);
        using var capture = new FindingsCapture(catalogue.PlatformMeter);

        recorder.RecordFinding(findingType);

        Assert.Equal([(1L, expectedTag)], capture.Measurements);
    }

    [Theory]
    [MemberData(nameof(AllFindingTypes))]
    public void RecordFinding_EveryFindingType_RecordsOneTaggedMeasurement(RecoveryFindingType findingType)
    {
        // ENG-03: FailedExecutionHealed had no tag, so the sweep threw KeyNotFoundException on every heal.
        using var catalogue = new Phase1MetricCatalogue();
        var recorder = new RecoveryFindingsRecorder(catalogue);
        using var capture = new FindingsCapture(catalogue.PlatformMeter);

        recorder.RecordFinding(findingType);

        var measurement = Assert.Single(capture.Measurements);
        Assert.False(string.IsNullOrEmpty(measurement.Kind));
    }

    [Fact]
    public void DimensionName_IsTheCataloguesRecoveryFindingsDimension()
    {
        // OPS-12: doc 11 §4.3 and the catalogue name the dimension `kind`; the recorder tagged `finding_type`.
        var definition = Phase1MetricCatalogue.BuildCatalogue().Single(m => m.Name == RecoveryFindingsRecorder.CounterName);

        Assert.Equal([RecoveryFindingsRecorder.DimensionName], definition.Dimensions);
    }

    [Fact]
    public void Constructor_NullCatalogue_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RecoveryFindingsRecorder(null!));

    /// <summary>Listens for <c>recovery.findings</c> on one meter instance and keeps each value with its <c>kind</c> tag.</summary>
    private sealed class FindingsCapture : IDisposable
    {
        private readonly MeterListener listener = new();

        public FindingsCapture(Meter meter)
        {
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument.Meter, meter) && instrument.Name == RecoveryFindingsRecorder.CounterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
                Measurements.Add((value, tags.ToArray().Single(t => t.Key == RecoveryFindingsRecorder.DimensionName).Value as string)));
            listener.Start();
        }

        public List<(long Value, string? Kind)> Measurements { get; } = [];

        public void Dispose() => listener.Dispose();
    }
}
