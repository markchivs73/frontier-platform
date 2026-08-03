namespace Frontier.Platform.Observability.Tests;

/// <summary>Tests for <see cref="ContextTierMetrics"/> (S3.4).</summary>
public sealed class ContextTierMetricsTests
{
    [Fact]
    public void CacheHit_ReportsFullHitRateAndTokenSavings()
    {
        var metrics = new ContextTierMetrics(
            Tier: "baseline",
            BytesSent: 1024,
            EstimatedTokensSent: 256,
            CacheHit: true,
            CacheBytesHit: 1024,
            CacheTokensHit: 256,
            CostDeltaTokens: 0.01m);

        Assert.Equal(100m, metrics.HitRatePercent);
        Assert.Equal(256, metrics.TokenSavings);
    }

    [Fact]
    public void CacheMiss_ReportsZeroHitRateAndNoTokenSavings()
    {
        var metrics = new ContextTierMetrics(
            Tier: "dynamic",
            BytesSent: 512,
            EstimatedTokensSent: 128,
            CacheHit: false,
            CacheBytesHit: 0,
            CacheTokensHit: 0,
            CostDeltaTokens: 0m);

        Assert.Equal(0m, metrics.HitRatePercent);
        Assert.Equal(0, metrics.TokenSavings);
    }

    [Fact]
    public void ValueEqualityAndAccessors()
    {
        var metrics = new ContextTierMetrics("baseline", 1024, 256, true, 1024, 256, 0.01m);

        Assert.Equal("baseline", metrics.Tier);
        Assert.Equal(1024, metrics.BytesSent);
        Assert.Equal(256, metrics.EstimatedTokensSent);
        Assert.True(metrics.CacheHit);
        Assert.Equal(1024, metrics.CacheBytesHit);
        Assert.Equal(256, metrics.CacheTokensHit);
        Assert.Equal(0.01m, metrics.CostDeltaTokens);

        Assert.Equal(metrics, metrics with { });
        Assert.NotEqual(metrics, metrics with { CacheHit = false });

        var (tier, bytesSent, estimatedTokensSent, cacheHit, cacheBytesHit, cacheTokensHit, costDeltaTokens) = metrics;
        Assert.Equal(metrics.Tier, tier);
        Assert.Equal(metrics.BytesSent, bytesSent);
        Assert.Equal(metrics.EstimatedTokensSent, estimatedTokensSent);
        Assert.Equal(metrics.CacheHit, cacheHit);
        Assert.Equal(metrics.CacheBytesHit, cacheBytesHit);
        Assert.Equal(metrics.CacheTokensHit, cacheTokensHit);
        Assert.Equal(metrics.CostDeltaTokens, costDeltaTokens);

        Assert.Contains("ContextTierMetrics", metrics.ToString(), StringComparison.Ordinal);
    }
}
