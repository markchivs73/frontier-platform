using System.Diagnostics.Metrics;
using Frontier.Platform.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>
/// Review-01 OPS-11 and DOC-19: the refresh counter is emitted on ADR-O1's one platform meter,
/// <c>Frontier.Platform</c>, which is the meter the consumer exports, under the catalogue's name
/// <c>context.refresh.events{reason}</c> (doc 11 §4.1). It used to go to a meter of its own as
/// <c>dynamic_context_refreshed</c>, which nothing exported and no dashboard looked for.
/// </summary>
public sealed class DynamicContextRefreshMetricsTests
{
    /// <summary>The meter name the consumer's OTEL bootstrap adds; the catalogue's <c>Phase1MetricCatalogue.MeterName</c>.</summary>
    private const string PlatformMeterName = "Frontier.Platform";

    private static DynamicContextRefresher Refresher(IEngagementContextStore store) =>
        new(store, NullLogger<DynamicContextRefresher>.Instance);

    [Fact]
    public async Task RefreshDynamicAsync_ChangedContent_CountsOneRefreshEventOnThePlatformMeter()
    {
        var reason = $"metrics-dynamic-{Guid.NewGuid():N}";
        using var capture = new RefreshCapture(reason);
        using var refresher = Refresher(new Phase1EngagementContextStore());

        await refresher.RefreshDynamicAsync(new EngagementId("eng-metrics-1"), """{"v":1}""", reason, CancellationToken.None);

        Assert.Equal([1], capture.Values);
    }

    [Fact]
    public async Task RefreshDynamicAsync_UnchangedContent_CountsNothing()
    {
        var reason = $"metrics-noop-{Guid.NewGuid():N}";
        var store = new Phase1EngagementContextStore();
        await store.UpsertDynamicContextAsync(new EngagementId("eng-metrics-2"), """{"v":1}""", CancellationToken.None);
        using var capture = new RefreshCapture(reason);
        using var refresher = Refresher(store);

        await refresher.RefreshDynamicAsync(new EngagementId("eng-metrics-2"), """{"v":1}""", reason, CancellationToken.None);

        Assert.Empty(capture.Values);
    }

    [Fact]
    public async Task RefreshComponentsAsync_NewContext_CountsOneRefreshEventOnThePlatformMeter()
    {
        var reason = $"metrics-components-{Guid.NewGuid():N}";
        using var capture = new RefreshCapture(reason);
        using var refresher = Refresher(new Phase1EngagementContextStore());
        var components = new Dictionary<string, string>(StringComparer.Ordinal) { ["client_profile"] = """{"a":1}""" };

        await refresher.RefreshComponentsAsync(new EngagementId("eng-metrics-3"), components, reason, CancellationToken.None);

        Assert.Equal([1], capture.Values);
    }

    [Fact]
    public void Names_MatchTheSharedMeterAndTheCatalogue()
    {
        Assert.Equal(PlatformMeterName, DynamicContextRefresher.MeterName);
        Assert.Equal("context.refresh.events", DynamicContextRefresher.CounterName);
    }

    /// <summary>
    /// Listens for <c>context.refresh.events</c> on any meter named <c>Frontier.Platform</c> and keeps
    /// the values tagged with one test's unique reason, so parallel tests never see each other's events.
    /// </summary>
    private sealed class RefreshCapture : IDisposable
    {
        private readonly MeterListener listener = new();

        public RefreshCapture(string reason)
        {
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PlatformMeterName && instrument.Name == "context.refresh.events")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
            {
                if (tags.ToArray().Any(t => t.Key == "reason" && Equals(t.Value, reason)))
                {
                    Values.Add(value);
                }
            });
            listener.Start();
        }

        public List<int> Values { get; } = [];

        public void Dispose() => listener.Dispose();
    }
}
