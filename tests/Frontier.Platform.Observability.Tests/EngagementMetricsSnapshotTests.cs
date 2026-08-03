namespace Frontier.Platform.Observability.Tests;

/// <summary>Tests for <see cref="EngagementMetricsSnapshot"/> (S3.4).</summary>
public sealed class EngagementMetricsSnapshotTests
{
    [Fact]
    public void CachedTokensPercent_WithTokensSent_ComputesRoundedPercentage()
    {
        var snapshot = new EngagementMetricsSnapshot(
            EngagementId: "eng-1",
            InvocationCount: 4,
            TotalTokensSent: 300,
            CachedTokensTotal: 100,
            CacheHitRatePercent: 25m,
            EstimatedCostSaved: 0.5m,
            SnapshotAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(25m, snapshot.EffectiveCacheHitRate);
        Assert.Equal(33.33m, snapshot.CachedTokensPercent);
    }

    [Fact]
    public void CachedTokensPercent_WithNoTokensSent_ReturnsZero()
    {
        var snapshot = new EngagementMetricsSnapshot(
            EngagementId: "eng-1",
            InvocationCount: 0,
            TotalTokensSent: 0,
            CachedTokensTotal: 0,
            CacheHitRatePercent: 0m,
            EstimatedCostSaved: 0m,
            SnapshotAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(0m, snapshot.CachedTokensPercent);
    }

    [Fact]
    public void ValueEqualityAndAccessors()
    {
        var snapshotAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var snapshot = new EngagementMetricsSnapshot("eng-1", 4, 300, 100, 25m, 0.5m, snapshotAt);

        Assert.Equal("eng-1", snapshot.EngagementId);
        Assert.Equal(4, snapshot.InvocationCount);
        Assert.Equal(300, snapshot.TotalTokensSent);
        Assert.Equal(100, snapshot.CachedTokensTotal);
        Assert.Equal(25m, snapshot.CacheHitRatePercent);
        Assert.Equal(0.5m, snapshot.EstimatedCostSaved);
        Assert.Equal(snapshotAt, snapshot.SnapshotAtUtc);

        Assert.Equal(snapshot, snapshot with { });
        Assert.NotEqual(snapshot, snapshot with { InvocationCount = 5 });

        var (engagementId, invocationCount, totalTokensSent, cachedTokensTotal, cacheHitRatePercent, estimatedCostSaved, snapshotAtUtc) = snapshot;
        Assert.Equal(snapshot.EngagementId, engagementId);
        Assert.Equal(snapshot.InvocationCount, invocationCount);
        Assert.Equal(snapshot.TotalTokensSent, totalTokensSent);
        Assert.Equal(snapshot.CachedTokensTotal, cachedTokensTotal);
        Assert.Equal(snapshot.CacheHitRatePercent, cacheHitRatePercent);
        Assert.Equal(snapshot.EstimatedCostSaved, estimatedCostSaved);
        Assert.Equal(snapshot.SnapshotAtUtc, snapshotAtUtc);

        Assert.Contains("EngagementMetricsSnapshot", snapshot.ToString(), StringComparison.Ordinal);
    }
}
