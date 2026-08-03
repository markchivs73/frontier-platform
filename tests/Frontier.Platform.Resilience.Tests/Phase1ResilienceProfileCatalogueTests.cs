namespace Frontier.Platform.Resilience.Tests;

/// <summary>S4.4/S6.7 tests for the compiled-in <see cref="Phase1ResilienceProfileCatalogue"/> (doc 10 §4).</summary>
public sealed class Phase1ResilienceProfileCatalogueTests
{
    [Fact]
    public void LlmDefault_MatchesDoc10WorkedExample()
    {
        var profile = Phase1ResilienceProfileCatalogue.LlmDefault;

        Assert.Equal("llm-default", profile.ProfileId);
        Assert.Equal(1, profile.Version);
        Assert.Equal(5, profile.InnerRetry.MaxAttempts);
        Assert.Equal("decorrelated-jitter", profile.InnerRetry.Backoff);
        Assert.Equal(1_000, profile.InnerRetry.BaseDelayMs);
        Assert.Equal(30_000, profile.InnerRetry.MaxDelayMs);
        Assert.Equal(90_000, profile.TimeoutMs);
        Assert.Equal(0.5, profile.CircuitBreaker.FailureRatio);
        Assert.Equal(10, profile.CircuitBreaker.MinThroughput);
        Assert.Equal(30, profile.CircuitBreaker.SamplingWindowSeconds);
        Assert.Equal(60, profile.CircuitBreaker.BreakDurationSeconds);
        Assert.Equal("provider", profile.Bulkhead.Scope);
        Assert.Equal(24, profile.Bulkhead.MaxConcurrent);
        Assert.Equal(48, profile.Bulkhead.MaxQueue);
        Assert.Equal(2, profile.OuterRetry.MaxAttempts);
    }

    [Fact]
    public void SnapshotPersistence_MatchesAdrS3()
    {
        var profile = Phase1ResilienceProfileCatalogue.SnapshotPersistence;

        Assert.Equal("snapshot-persistence", profile.ProfileId);
        Assert.Equal(1, profile.Version);
        Assert.Equal(10, profile.InnerRetry.MaxAttempts);
        Assert.Equal("decorrelated-jitter", profile.InnerRetry.Backoff);
        Assert.Equal(1_000, profile.InnerRetry.BaseDelayMs);
        Assert.Equal(300_000, profile.InnerRetry.MaxDelayMs);
        Assert.Equal(5_000, profile.TimeoutMs);
        Assert.Equal(0.5, profile.CircuitBreaker.FailureRatio);
        Assert.Equal(10, profile.CircuitBreaker.MinThroughput);
        Assert.Equal(30, profile.CircuitBreaker.SamplingWindowSeconds);
        Assert.Equal(60, profile.CircuitBreaker.BreakDurationSeconds);
        Assert.Equal("provider", profile.Bulkhead.Scope);
        Assert.Equal(48, profile.Bulkhead.MaxConcurrent);
        Assert.Equal(96, profile.Bulkhead.MaxQueue);
        Assert.Equal(1, profile.OuterRetry.MaxAttempts);
    }

    [Fact]
    public void ByProfileId_ContainsAllSevenPhase1Profiles()
    {
        Assert.Same(Phase1ResilienceProfileCatalogue.LlmDefault, Phase1ResilienceProfileCatalogue.ByProfileId["llm-default"]);
        Assert.Same(Phase1ResilienceProfileCatalogue.SnapshotPersistence, Phase1ResilienceProfileCatalogue.ByProfileId["snapshot-persistence"]);
        Assert.Same(Phase1ResilienceProfileCatalogue.LlmInteractive, Phase1ResilienceProfileCatalogue.ByProfileId["llm-interactive"]);
        Assert.Same(Phase1ResilienceProfileCatalogue.McpRead, Phase1ResilienceProfileCatalogue.ByProfileId["mcp-read"]);
        Assert.Same(Phase1ResilienceProfileCatalogue.McpWrite, Phase1ResilienceProfileCatalogue.ByProfileId["mcp-write"]);
        Assert.Same(Phase1ResilienceProfileCatalogue.Storage, Phase1ResilienceProfileCatalogue.ByProfileId["storage"]);
        Assert.Same(Phase1ResilienceProfileCatalogue.None, Phase1ResilienceProfileCatalogue.ByProfileId["none"]);
        Assert.Equal(7, Phase1ResilienceProfileCatalogue.ByProfileId.Count);
    }

    [Fact]
    public void LlmInteractive_MatchesDoc10Spec()
    {
        var profile = Phase1ResilienceProfileCatalogue.LlmInteractive;

        Assert.Equal("llm-interactive", profile.ProfileId);
        Assert.Equal(2, profile.InnerRetry.MaxAttempts);
        Assert.Equal(20_000, profile.TimeoutMs);
        Assert.Equal(1, profile.OuterRetry.MaxAttempts);
    }

    [Fact]
    public void McpRead_MatchesDoc10Spec()
    {
        var profile = Phase1ResilienceProfileCatalogue.McpRead;

        Assert.Equal("mcp-read", profile.ProfileId);
        Assert.Equal(3, profile.InnerRetry.MaxAttempts);
        Assert.Equal(10_000, profile.TimeoutMs);
    }

    [Fact]
    public void McpWrite_MatchesDoc10Spec()
    {
        var profile = Phase1ResilienceProfileCatalogue.McpWrite;

        Assert.Equal("mcp-write", profile.ProfileId);
        Assert.Equal(3, profile.InnerRetry.MaxAttempts);
        Assert.Equal(10_000, profile.TimeoutMs);
    }

    [Fact]
    public void Storage_HasSingleInnerAttemptDeferringToSdk()
    {
        var profile = Phase1ResilienceProfileCatalogue.Storage;

        Assert.Equal("storage", profile.ProfileId);
        Assert.Equal(1, profile.InnerRetry.MaxAttempts);
        Assert.Equal(1, profile.OuterRetry.MaxAttempts);
    }

    [Fact]
    public void None_HasSingleAttemptAndEffectivelyNoBreakerOrBulkhead()
    {
        var profile = Phase1ResilienceProfileCatalogue.None;

        Assert.Equal("none", profile.ProfileId);
        Assert.Equal(1, profile.InnerRetry.MaxAttempts);
        Assert.Equal(1, profile.OuterRetry.MaxAttempts);
        Assert.Equal(1.0, profile.CircuitBreaker.FailureRatio);
        Assert.True(profile.CircuitBreaker.MinThroughput >= 1_000_000);
        Assert.True(profile.Bulkhead.MaxConcurrent >= 1_000);
    }
}
