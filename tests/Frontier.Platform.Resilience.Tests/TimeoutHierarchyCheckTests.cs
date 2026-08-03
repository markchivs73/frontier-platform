namespace Frontier.Platform.Resilience.Tests;

/// <summary>S4.7b tests for <see cref="TimeoutHierarchyCheck"/> (doc 12 §6, doc 10 §7).</summary>
public sealed class TimeoutHierarchyCheckTests
{
    [Fact]
    public void Name_ReturnsTimeoutHierarchy()
    {
        var check = new TimeoutHierarchyCheck();

        Assert.Equal("TimeoutHierarchy", check.Name);
    }

    [Fact]
    public async Task CheckAsync_Phase1Catalogue_ReturnsPass()
    {
        var check = new TimeoutHierarchyCheck();

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Evaluate_AllProfilesWithinHierarchy_ReturnsPass()
    {
        var result = TimeoutHierarchyCheck.Evaluate(Phase1ResilienceProfileCatalogue.ByProfileId.Values);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Evaluate_SingleAttemptProfile_SkipsPerAttemptCheck_ReturnsPass()
    {
        // For MaxAttempts==1, PipelineTimeoutMs == TimeoutMs by definition — the two timeouts
        // are identical, not a hierarchy violation (doc 10 §7).
        var profile = BuildProfile("single-attempt", timeoutMs: 1_000, maxAttempts: 1);

        var result = TimeoutHierarchyCheck.Evaluate([profile]);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Evaluate_PipelineTimeoutNotLessThanDtfActivityTimeout_ReturnsFail()
    {
        var profile = BuildProfile("bad-pipeline", timeoutMs: 100_000, maxAttempts: 10);

        var result = TimeoutHierarchyCheck.Evaluate([profile]);

        Assert.False(result.Passed);
        Assert.Contains("pipeline timeout", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void PipelineTimeoutMs_MultipliesTimeoutByMaxAttempts()
    {
        var profile = BuildProfile("p", timeoutMs: 90_000, maxAttempts: 5);

        Assert.Equal(450_000, TimeoutHierarchyCheck.PipelineTimeoutMs(profile));
    }

    internal static ResilienceProfile BuildProfile(string profileId, int timeoutMs, int maxAttempts) => new()
    {
        ProfileId = profileId,
        Version = 1,
        InnerRetry = new InnerRetrySpec
        {
            MaxAttempts = maxAttempts,
            Backoff = "decorrelated-jitter",
            BaseDelayMs = 1_000,
            MaxDelayMs = 30_000,
        },
        TimeoutMs = timeoutMs,
        CircuitBreaker = new CircuitBreakerSpec
        {
            FailureRatio = 0.5,
            MinThroughput = 10,
            SamplingWindowSeconds = 30,
            BreakDurationSeconds = 60,
        },
        Bulkhead = new BulkheadSpec
        {
            Scope = "provider",
            MaxConcurrent = 24,
            MaxQueue = 48,
        },
        OuterRetry = new OuterRetrySpec
        {
            MaxAttempts = 2,
        },
    };
}
