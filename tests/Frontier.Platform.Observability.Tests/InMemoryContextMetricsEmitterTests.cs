namespace Frontier.Platform.Observability.Tests;

/// <summary>Tests for <see cref="InMemoryContextMetricsEmitter"/> (S3.4 PoC).</summary>
public sealed class InMemoryContextMetricsEmitterTests
{
    private readonly InMemoryContextMetricsEmitter emitter = new();

    [Fact]
    public async Task EmitTierMetricsAsync_NullExecutionId_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            emitter.EmitTierMetricsAsync(null!, []));

    [Fact]
    public async Task EmitTierMetricsAsync_NullMetrics_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            emitter.EmitTierMetricsAsync("exec-1", null!));

    [Fact]
    public async Task GetEngagementMetricsAsync_NullEngagementId_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            emitter.GetEngagementMetricsAsync(null!));

    [Fact]
    public async Task GetEngagementMetricsAsync_NoSnapshotRecorded_ReturnsNull() =>
        Assert.Null(await emitter.GetEngagementMetricsAsync("eng-1"));

    [Fact]
    public async Task UpdateEngagementSnapshot_NoMetricsRecorded_DoesNotCreateSnapshot()
    {
        emitter.UpdateEngagementSnapshot("eng-1");

        Assert.Null(await emitter.GetEngagementMetricsAsync("eng-1"));
    }

    [Fact]
    public async Task EmitThenUpdate_ComputesAggregatedSnapshot()
    {
        IReadOnlyList<ContextTierMetrics> metrics =
        [
            new("baseline", 1000, 250, CacheHit: true, CacheBytesHit: 1000, CacheTokensHit: 250, CostDeltaTokens: 0.02m),
            new("dynamic", 500, 125, CacheHit: false, CacheBytesHit: 0, CacheTokensHit: 0, CostDeltaTokens: 0m),
        ];

        await emitter.EmitTierMetricsAsync("exec-1", metrics);
        emitter.UpdateEngagementSnapshot("eng-1");

        var snapshot = await emitter.GetEngagementMetricsAsync("eng-1");

        Assert.NotNull(snapshot);
        Assert.Equal("eng-1", snapshot.EngagementId);
        Assert.Equal(2, snapshot.InvocationCount);
        Assert.Equal(375, snapshot.TotalTokensSent);
        Assert.Equal(250, snapshot.CachedTokensTotal);
        Assert.Equal(50m, snapshot.CacheHitRatePercent);
        Assert.Equal(0.02m, snapshot.EstimatedCostSaved);
    }
}
