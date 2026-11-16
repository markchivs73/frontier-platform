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

    /// <summary>ADR-PA48: the transport profiles are a set of their own, and no node profile is in it.</summary>
    [Fact]
    public void HttpTransportByProfileId_HoldsOnlyTheTwoTransportProfiles_ApartFromTheNodeProfiles()
    {
        Assert.Same(Phase1ResilienceProfileCatalogue.McpTransport, Phase1ResilienceProfileCatalogue.HttpTransportByProfileId["mcp-transport"]);
        Assert.Same(Phase1ResilienceProfileCatalogue.A2aTransport, Phase1ResilienceProfileCatalogue.HttpTransportByProfileId["a2a-transport"]);
        Assert.Equal(2, Phase1ResilienceProfileCatalogue.HttpTransportByProfileId.Count);
        Assert.Empty(Phase1ResilienceProfileCatalogue.HttpTransportByProfileId.Keys.Intersect(Phase1ResilienceProfileCatalogue.ByProfileId.Keys));
        Assert.All(Phase1ResilienceProfileCatalogue.HttpTransportByProfileId.Values, profile => Assert.Equal(1, profile.InnerRetry.MaxAttempts));
    }

    /// <summary>ADR-PA48 (consumer S13.246): the HTTP transport profiles make one attempt, with mcp-read's breaker and bulkhead.</summary>
    [Theory]
    [InlineData("mcp-transport", 30_000)]
    [InlineData("a2a-transport", 80_000)]
    public void TransportProfiles_MakeOneAttempt_WithMcpReadsBreakerAndBulkhead(string profileId, int timeoutMs)
    {
        var profile = Phase1ResilienceProfileCatalogue.HttpTransportByProfileId[profileId];

        Assert.Equal(profileId, profile.ProfileId);
        Assert.Equal(1, profile.Version);
        Assert.Equal(1, profile.InnerRetry.MaxAttempts);
        Assert.Equal(timeoutMs, profile.TimeoutMs);
        Assert.Equal(Phase1ResilienceProfileCatalogue.McpRead.CircuitBreaker, profile.CircuitBreaker);
        Assert.Equal(Phase1ResilienceProfileCatalogue.McpRead.Bulkhead with { Scope = "authority" }, profile.Bulkhead);
        Assert.Equal(1, profile.OuterRetry.MaxAttempts);
    }

    /// <summary>ADR-PA48: each transport profile's total timeout (3x per attempt) stays under the DTF activity timeout (doc 10 §7).</summary>
    [Fact]
    public void TransportProfiles_PassTheTimeoutHierarchyCheck()
    {
        var result = TimeoutHierarchyCheck.Evaluate(Phase1ResilienceProfileCatalogue.HttpTransportByProfileId.Values);

        Assert.True(result.Passed, result.FailureReason);
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
    }

    /// <summary>S13.247: v2 re-runs a store-backed activity in the outer DTF loop, so one passing store fault does not fail the run.</summary>
    [Fact]
    public void Storage_V2_RetriesInTheOuterLoopWithASecondScaleInterval()
    {
        var profile = Phase1ResilienceProfileCatalogue.Storage;

        Assert.Equal(2, profile.Version);
        Assert.Equal(3, profile.OuterRetry.MaxAttempts);
        Assert.Equal(1_000, profile.InnerRetry.BaseDelayMs);
        Assert.Equal(30_000, profile.InnerRetry.MaxDelayMs);
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

    /// <summary>S13.217: v2's per-attempt timeout keeps the 3× total under the 10-minute activity timeout (doc 10 §7).</summary>
    [Fact]
    public void None_V2_TotalTimeoutStaysUnderTheActivityTimeout()
    {
        var profile = Phase1ResilienceProfileCatalogue.None;

        Assert.Equal(2, profile.Version);
        Assert.Equal(180_000, profile.TimeoutMs);
        Assert.True(ResiliencePolicyProvider.TotalTimeoutMs(profile) < TimeoutHierarchyCheck.DtfActivityTimeoutMs);
    }
}
