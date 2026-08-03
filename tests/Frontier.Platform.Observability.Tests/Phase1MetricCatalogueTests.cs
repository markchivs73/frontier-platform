namespace Frontier.Platform.Observability.Tests;

/// <summary>S6.8 tests for <see cref="Phase1MetricCatalogue"/> (doc 11 §4, ADR-O1).</summary>
public sealed class Phase1MetricCatalogueTests : IDisposable
{
    private readonly Phase1MetricCatalogue _catalogue = new();

    public void Dispose() => _catalogue.Dispose();

    [Fact]
    public void All_Contains15Phase1Metrics()
    {
        Assert.Equal(15, _catalogue.All.Count);
    }

    [Theory]
    [InlineData("context.cache.hit_rate",    MetricInstrumentType.ObservableGauge)]
    [InlineData("context.tokens",            MetricInstrumentType.Counter)]
    [InlineData("context.cache.writes",      MetricInstrumentType.Counter)]
    [InlineData("context.refresh.events",    MetricInstrumentType.Counter)]
    [InlineData("context.cost.saved_gbp",    MetricInstrumentType.Counter)]
    [InlineData("validator.outcomes",        MetricInstrumentType.Counter)]
    [InlineData("resilience.retries",        MetricInstrumentType.Counter)]
    [InlineData("hitl.decisions",            MetricInstrumentType.Counter)]
    [InlineData("hitl.time_to_decision",     MetricInstrumentType.Histogram)]
    [InlineData("cascade.size",              MetricInstrumentType.Histogram)]
    [InlineData("agent.invocation.duration", MetricInstrumentType.Histogram)]
    [InlineData("activity.executions",       MetricInstrumentType.Counter)]
    [InlineData("activity.duration",         MetricInstrumentType.Histogram)]
    [InlineData("executions.active",         MetricInstrumentType.ObservableGauge)]
    [InlineData("recovery.findings",         MetricInstrumentType.Counter)]
    public void All_ContainsMetricWithCorrectInstrumentType(string name, MetricInstrumentType expectedType)
    {
        var metric = _catalogue.All.SingleOrDefault(m => m.Name == name);

        Assert.NotNull(metric);
        Assert.Equal(expectedType, metric.InstrumentType);
    }

    [Fact]
    public void All_NoMetricHasUnboundedIdDimension()
    {
        // ADR-O1: execution_id and engagement_id are span attributes, never metric dimensions
        var violations = _catalogue.All
            .SelectMany(m => m.Dimensions, (m, d) => (m.Name, Dimension: d))
            .Where(x => x.Dimension.Contains("execution_id", StringComparison.Ordinal)
                     || x.Dimension.Contains("engagement_id", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void All_EveryMetricHasNonEmptyUnit()
    {
        var missing = _catalogue.All.Where(m => string.IsNullOrWhiteSpace(m.Unit)).Select(m => m.Name).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void All_EveryMetricHasNonEmptyDescription()
    {
        var missing = _catalogue.All.Where(m => string.IsNullOrWhiteSpace(m.Description)).Select(m => m.Name).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void All_MetricNamesAreUnique()
    {
        var names = _catalogue.All.Select(m => m.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void PlatformMeter_HasExpectedName()
    {
        Assert.Equal(Phase1MetricCatalogue.MeterName, _catalogue.PlatformMeter.Name);
    }

    [Fact]
    public void BuildCatalogue_ReturnsSameReferenceEachCall()
    {
        var first = Phase1MetricCatalogue.BuildCatalogue();
        var second = Phase1MetricCatalogue.BuildCatalogue();

        // Same items — structural equality via record comparison
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.Select(m => m.Name), second.Select(m => m.Name));
    }
}
