using Frontier.Platform.Serialization;
using Frontier.Platform.Abstractions;
using Xunit;

namespace Frontier.Platform.ContextAssembly.Tests;

public sealed class ContextDebuggerComparisonTests
{
    [Fact]
    public async Task CompareAsync_IdenticalPackages_NoChanges()
    {
        var debugger = new ContextDebugger();
        var package = new ContextPackage
        {
            Baseline = new BaselineTier
            {
                BaselineVersion = "2026.06.1",
                Components = new[] { "comp-a" },
                Content = """{"baseline":"data"}""",
            },
            Dynamic = new DynamicTier
            {
                EngagementId = "eng-1",
                DynamicEpoch = 0,
                AssembledFromSnapshotRef = "snap-ref",
                Content = """{"dynamic":"data"}""",
            },
            RealTime = null,
            Hints = new CacheHint
            {
                BreakpointAfterBaseline = 1024,
                BreakpointAfterDynamic = 1024,
                BaselineCacheKey = "baseline-key",
                DynamicCacheKey = "dynamic-key",
            },
        };

        var result = await debugger.CompareAsync(package, package, CancellationToken.None);

        Assert.False(result.BaselineComparison.ChangedFromPrevious);
        Assert.False(result.DynamicComparison.ChangedFromPrevious);
        Assert.Null(result.RealTimeComparison);
    }

    [Fact]
    public async Task CompareAsync_NoPrevious_MarkAsChanged()
    {
        var debugger = new ContextDebugger();
        var package = new ContextPackage
        {
            Baseline = new BaselineTier
            {
                BaselineVersion = "2026.06.1",
                Components = new[] { "comp-a" },
                Content = """{"baseline":"data"}""",
            },
            Dynamic = new DynamicTier
            {
                EngagementId = "eng-1",
                DynamicEpoch = 0,
                AssembledFromSnapshotRef = "snap-ref",
                Content = """{"dynamic":"data"}""",
            },
            RealTime = null,
            Hints = new CacheHint
            {
                BreakpointAfterBaseline = 1024,
                BreakpointAfterDynamic = 1024,
                BaselineCacheKey = "baseline-key",
                DynamicCacheKey = "dynamic-key",
            },
        };

        var result = await debugger.CompareAsync(package, previous: null, CancellationToken.None);

        Assert.True(result.BaselineComparison.ChangedFromPrevious);
        Assert.True(result.DynamicComparison.ChangedFromPrevious);
        Assert.Null(result.RealTimeComparison);
    }

    [Fact]
    public async Task CompareAsync_DifferentDynamicContent_Detected()
    {
        var debugger = new ContextDebugger();
        var current = new ContextPackage
        {
            Baseline = new BaselineTier
            {
                BaselineVersion = "2026.06.1",
                Components = new[] { "comp-a" },
                Content = """{"baseline":"data"}""",
            },
            Dynamic = new DynamicTier
            {
                EngagementId = "eng-1",
                DynamicEpoch = 1,
                AssembledFromSnapshotRef = "snap-ref",
                Content = """{"dynamic":"new"}""",
            },
            RealTime = null,
            Hints = new CacheHint
            {
                BreakpointAfterBaseline = 1024,
                BreakpointAfterDynamic = 1024,
                BaselineCacheKey = "baseline-key",
                DynamicCacheKey = "dynamic-key",
            },
        };

        var previous = new ContextPackage
        {
            Baseline = new BaselineTier
            {
                BaselineVersion = "2026.06.1",
                Components = new[] { "comp-a" },
                Content = """{"baseline":"data"}""",
            },
            Dynamic = new DynamicTier
            {
                EngagementId = "eng-1",
                DynamicEpoch = 0,
                AssembledFromSnapshotRef = "snap-ref",
                Content = """{"dynamic":"old"}""",
            },
            RealTime = null,
            Hints = new CacheHint
            {
                BreakpointAfterBaseline = 1024,
                BreakpointAfterDynamic = 1024,
                BaselineCacheKey = "baseline-key",
                DynamicCacheKey = "dynamic-key",
            },
        };

        var result = await debugger.CompareAsync(current, previous, CancellationToken.None);

        Assert.False(result.BaselineComparison.ChangedFromPrevious);
        Assert.True(result.DynamicComparison.ChangedFromPrevious);
    }

    [Fact]
    public async Task CompareAsync_BothHaveRealTimeWithSameContent_NotChanged()
    {
        // previous?.RealTime is null ? ... : current.RealTime is null || hash != hash(previous) —
        // every other test leaves previous.RealTime null, so the ternary's false branch (both
        // populated) was never exercised (S9.24 branch-coverage gap).
        var debugger = new ContextDebugger();
        var package = SamplePackage(realTimeContent: """{"rt":"same"}""");

        var result = await debugger.CompareAsync(package, package, CancellationToken.None);

        Assert.NotNull(result.RealTimeComparison);
        Assert.False(result.RealTimeComparison.ChangedFromPrevious);
    }

    [Fact]
    public async Task CompareAsync_BothHaveRealTimeWithDifferentContent_Changed()
    {
        var debugger = new ContextDebugger();
        var previous = SamplePackage(realTimeContent: """{"rt":"old"}""");
        var current = SamplePackage(realTimeContent: """{"rt":"new"}""");

        var result = await debugger.CompareAsync(current, previous, CancellationToken.None);

        Assert.NotNull(result.RealTimeComparison);
        Assert.True(result.RealTimeComparison.ChangedFromPrevious);
    }

    [Fact]
    public async Task CompareAsync_PreviousHadRealTimeCurrentDoesNot_Changed()
    {
        var debugger = new ContextDebugger();
        var previous = SamplePackage(realTimeContent: """{"rt":"old"}""");
        var current = SamplePackage(realTimeContent: null);

        var result = await debugger.CompareAsync(current, previous, CancellationToken.None);

        Assert.Null(result.RealTimeComparison);
    }

    private static ContextPackage SamplePackage(string? realTimeContent) => new()
    {
        Baseline = new BaselineTier { BaselineVersion = "2026.06.1", Components = ["comp-a"], Content = """{"baseline":"data"}""" },
        Dynamic = new DynamicTier { EngagementId = "eng-1", DynamicEpoch = 0, AssembledFromSnapshotRef = "snap-ref", Content = """{"dynamic":"data"}""" },
        RealTime = realTimeContent is null ? null : new RealTimeTier
        {
            Fetches = [new RealTimeFetch { SourceId = "source-1", FetchedAtUtc = DateTime.UtcNow, IsStale = false }],
            Content = realTimeContent,
        },
        Hints = new CacheHint { BreakpointAfterBaseline = 1024, BreakpointAfterDynamic = 1024, BaselineCacheKey = "baseline-key", DynamicCacheKey = "dynamic-key" },
    };

    [Fact]
    public async Task CompareAsync_RealTimeFetchCount_Captured()
    {
        var debugger = new ContextDebugger();
        var current = new ContextPackage
        {
            Baseline = new BaselineTier
            {
                BaselineVersion = "2026.06.1",
                Components = new[] { "comp-a" },
                Content = """{"baseline":"data"}""",
            },
            Dynamic = new DynamicTier
            {
                EngagementId = "eng-1",
                DynamicEpoch = 0,
                AssembledFromSnapshotRef = "snap-ref",
                Content = """{"dynamic":"data"}""",
            },
            RealTime = new RealTimeTier
            {
                Fetches = new[]
                {
                    new RealTimeFetch { SourceId = "source-1", FetchedAtUtc = DateTime.UtcNow, IsStale = false },
                    new RealTimeFetch { SourceId = "source-2", FetchedAtUtc = DateTime.UtcNow, IsStale = false },
                },
                Content = """{"rt":"data"}""",
            },
            Hints = new CacheHint
            {
                BreakpointAfterBaseline = 1024,
                BreakpointAfterDynamic = 1024,
                BaselineCacheKey = "baseline-key",
                DynamicCacheKey = "dynamic-key",
            },
        };

        var result = await debugger.CompareAsync(current, previous: null, CancellationToken.None);

        Assert.NotNull(result.RealTimeComparison);
        Assert.Equal(2, result.RealTimeComparison.FetchCount);
    }

    [Fact]
    public async Task CompareAsync_HashesCanonical()
    {
        var debugger = new ContextDebugger();
        var content = """{"test":"data"}""";
        var expectedHash = CanonicalProfile.Hash(content);

        var package = new ContextPackage
        {
            Baseline = new BaselineTier
            {
                BaselineVersion = "2026.06.1",
                Components = new[] { "comp" },
                Content = content,
            },
            Dynamic = new DynamicTier
            {
                EngagementId = "eng-1",
                DynamicEpoch = 0,
                AssembledFromSnapshotRef = "ref",
                Content = content,
            },
            RealTime = null,
            Hints = new CacheHint
            {
                BreakpointAfterBaseline = 1024,
                BreakpointAfterDynamic = 1024,
                BaselineCacheKey = "baseline-key",
                DynamicCacheKey = "dynamic-key",
            },
        };

        var result = await debugger.CompareAsync(package, previous: null, CancellationToken.None);

        Assert.Equal(expectedHash, result.BaselineComparison.ContentHash);
        Assert.Equal(expectedHash, result.DynamicComparison.ContentHash);
    }
}
