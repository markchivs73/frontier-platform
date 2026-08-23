using Frontier.TestSupport;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S5.4 tests for <see cref="CacheMetricsAggregator"/> (doc 05 §4 step 2, C-15).</summary>
public sealed class CacheMetricsAggregatorTests
{
    [Fact]
    public void Aggregate_NoRecords_ReturnsZeroedMetricsWithoutDivideByZero()
    {
        var metrics = CacheMetricsAggregator.Aggregate([]);

        Assert.Equal(0, metrics.Baseline.Reads);
        Assert.Equal(0, metrics.Baseline.Writes);
        Assert.Equal(0m, metrics.Baseline.HitRatePercent);
        Assert.Equal(0, metrics.Baseline.TokensRead);
        Assert.Equal(0m, metrics.Dynamic.HitRatePercent);
        Assert.Equal(0m, metrics.RealTime.HitRatePercent);
    }

    [Fact]
    public void Aggregate_MixedCacheChangedFlags_CountsReadsAndWritesPerTier()
    {
        var unchanged = TelemetrySamples.Record() with
        {
            CorrelationId = "corr-a",
            BaselineCacheChanged = false,
            DynamicCacheChanged = false,
            RealTimeCacheChanged = false,
            CacheReadTokens = 1000,
        };
        var changed = unchanged with
        {
            CorrelationId = "corr-b",
            BaselineCacheChanged = true,
            DynamicCacheChanged = true,
            RealTimeCacheChanged = true,
            CacheReadTokens = 500,
        };

        var metrics = CacheMetricsAggregator.Aggregate([unchanged, changed]);

        Assert.Equal(1, metrics.Baseline.Reads);
        Assert.Equal(1, metrics.Baseline.Writes);
        Assert.Equal(50m, metrics.Baseline.HitRatePercent);
        Assert.Equal(1, metrics.Dynamic.Reads);
        Assert.Equal(1, metrics.Dynamic.Writes);
        Assert.Equal(1, metrics.RealTime.Reads);
        Assert.Equal(1, metrics.RealTime.Writes);
    }

    [Fact]
    public void Aggregate_AttributesAggregateCacheReadTokensWhollyToBaseline()
    {
        var first = TelemetrySamples.Record() with { CorrelationId = "corr-a", CacheReadTokens = 1000 };
        var second = first with { CorrelationId = "corr-b", CacheReadTokens = 500 };

        var metrics = CacheMetricsAggregator.Aggregate([first, second]);

        Assert.Equal(1500, metrics.Baseline.TokensRead);
        Assert.Equal(0, metrics.Dynamic.TokensRead);
        Assert.Equal(0, metrics.RealTime.TokensRead);
    }

    [Fact]
    public void AggregateTier_AllChanged_HitRateIsZero()
    {
        var records = new[]
        {
            TelemetrySamples.Record() with { BaselineCacheChanged = true },
        };

        var tier = CacheMetricsAggregator.AggregateTier(records, record => record.BaselineCacheChanged, tokensRead: 0);

        Assert.Equal(0, tier.Reads);
        Assert.Equal(1, tier.Writes);
        Assert.Equal(0m, tier.HitRatePercent);
    }

    [Fact]
    public void AggregateTier_AllUnchanged_HitRateIsFullyHundredPercent()
    {
        var records = new[]
        {
            TelemetrySamples.Record() with { BaselineCacheChanged = false },
            TelemetrySamples.Record() with { CorrelationId = "corr-4", BaselineCacheChanged = false },
        };

        var tier = CacheMetricsAggregator.AggregateTier(records, record => record.BaselineCacheChanged, tokensRead: 0);

        Assert.Equal(2, tier.Reads);
        Assert.Equal(0, tier.Writes);
        Assert.Equal(100m, tier.HitRatePercent);
    }
}
