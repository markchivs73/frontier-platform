using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>Tests for <see cref="ContextAssemblerSimple"/> (S3.3 ADR-CR1).</summary>
public sealed class ContextAssemblerSimpleTests
{
    [Fact]
    public void Constructor_NullRegistry_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ContextAssemblerSimple(null!));

    [Fact]
    public async Task AssembleAsync_NullMetadata_Throws()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(NoCachingStrategy.Instance));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            assembler.AssembleAsync(null!, "baseline", "dynamic", "real-time"));
    }

    [Fact]
    public async Task AssembleAsync_NullBaselineContent_Throws()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(NoCachingStrategy.Instance));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            assembler.AssembleAsync(ContextAssemblyTestData.Metadata(), null!, "dynamic", "real-time"));
    }

    [Fact]
    public async Task AssembleAsync_NullDynamicContent_Throws()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(NoCachingStrategy.Instance));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            assembler.AssembleAsync(ContextAssemblyTestData.Metadata(), "baseline", null!, "real-time"));
    }

    [Fact]
    public async Task AssembleAsync_NullRealTimeContent_Throws()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(NoCachingStrategy.Instance));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            assembler.AssembleAsync(ContextAssemblyTestData.Metadata(), "baseline", "dynamic", null!));
    }

    [Fact]
    public async Task AssembleAsync_StrategyResolved_AppliesCacheDirectives()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(new AnthropicCachingStrategy()));

        var package = await assembler.AssembleAsync(
            ContextAssemblyTestData.Metadata(), "baseline content", "dynamic content", "real-time content");

        Assert.Equal("1.0", package.Baseline.BaselineVersion);
        Assert.Equal("baseline content", package.Baseline.Content);
        Assert.Equal("dynamic content", package.Dynamic.Content);
        Assert.Equal("real-time content", package.RealTime!.Content);
        Assert.NotNull(package.Hints);
        Assert.True(package.Hints.BreakpointAfterBaseline > 0);
    }

    [Fact]
    public async Task AssembleAsync_ComputesCacheHints_BreakpointsCorrectly()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(NoCachingStrategy.Instance));
        var baseline = "baseline";
        var dynamic = "dynamic";
        var realTime = "realtime";

        var package = await assembler.AssembleAsync(
            ContextAssemblyTestData.Metadata(), baseline, dynamic, realTime);

        Assert.Equal(baseline.Length, package.Hints.BreakpointAfterBaseline);
        Assert.Equal(baseline.Length + dynamic.Length, package.Hints.BreakpointAfterDynamic);
    }

    [Fact]
    public async Task AssembleAsync_ComputesCacheHints_WithEmptyTiers()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(NoCachingStrategy.Instance));

        var package = await assembler.AssembleAsync(
            ContextAssemblyTestData.Metadata(), "", "", "");

        Assert.Equal(0, package.Hints.BreakpointAfterBaseline);
        Assert.Equal(0, package.Hints.BreakpointAfterDynamic);
        Assert.NotNull(package.Hints.BaselineCacheKey);
        Assert.NotNull(package.Hints.DynamicCacheKey);
    }

    [Fact]
    public async Task AssembleAsync_WithEmptyRealTimeContent_SetsRealTimeTierToNull()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(NoCachingStrategy.Instance));

        var package = await assembler.AssembleAsync(
            ContextAssemblyTestData.Metadata(), "baseline", "dynamic", "");

        Assert.Null(package.RealTime);
    }

    [Fact]
    public async Task AssembleAsync_WithNonEmptyRealTimeContent_SetsRealTimeTier()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(NoCachingStrategy.Instance));

        var package = await assembler.AssembleAsync(
            ContextAssemblyTestData.Metadata(), "baseline", "dynamic", "real-time");

        Assert.NotNull(package.RealTime);
        Assert.Equal("real-time", package.RealTime.Content);
    }

    [Fact]
    public async Task AssembleAsync_StrategyIsNull_ReturnPackageUnmodified()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(null));

        var package = await assembler.AssembleAsync(
            ContextAssemblyTestData.Metadata(), "baseline", "dynamic", "real-time");

        Assert.NotNull(package);
        Assert.Equal("baseline", package.Baseline.Content);
    }

    [Fact]
    public async Task AssembleAsync_WithNullStrategyRegistry_ThrowsArgumentNull() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new ContextAssemblerSimple(null!).AssembleAsync(
                ContextAssemblyTestData.Metadata(), "b", "d", "r"));

    [Fact]
    public async Task AssembleAsync_RealTimeTierWithWhitespace_PreservesWhitespace()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(NoCachingStrategy.Instance));

        var package = await assembler.AssembleAsync(
            ContextAssemblyTestData.Metadata(), "baseline", "dynamic", "   ");

        Assert.NotNull(package.RealTime);
        Assert.Equal("   ", package.RealTime.Content);
    }

    [Fact]
    public async Task AssembleAsync_AllTiersWithContent_PreservesExactContent()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(NoCachingStrategy.Instance));
        var baseline = "baseline-content-with-special-chars-!@#$";
        var dynamic = "dynamic-data-123";
        var realTime = "realtime-signal-xyz";

        var package = await assembler.AssembleAsync(
            ContextAssemblyTestData.Metadata(), baseline, dynamic, realTime);

        Assert.Equal(baseline, package.Baseline.Content);
        Assert.Equal(dynamic, package.Dynamic.Content);
        Assert.Equal(realTime, package.RealTime!.Content);
    }

    [Fact]
    public async Task AssembleAsync_LargeTierContent_PreservesLength()
    {
        var assembler = new ContextAssemblerSimple(new FakeCachingStrategyRegistry(NoCachingStrategy.Instance));
        var baseline = new string('a', 10000);
        var dynamic = new string('b', 5000);
        var realTime = new string('c', 2000);

        var package = await assembler.AssembleAsync(
            ContextAssemblyTestData.Metadata(), baseline, dynamic, realTime);

        Assert.Equal(baseline.Length, package.Hints.BreakpointAfterBaseline);
        Assert.Equal(baseline.Length + dynamic.Length, package.Hints.BreakpointAfterDynamic);
    }

    [Fact]
    public async Task AssembleAsync_MultipleStrategyResolutions_ConsistentBehavior()
    {
        var registry = new FakeCachingStrategyRegistry(new AnthropicCachingStrategy());
        var assembler = new ContextAssemblerSimple(registry);
        var metadata = ContextAssemblyTestData.Metadata();

        // Call multiple times with different content lengths to ensure hints are computed correctly
        var package1 = await assembler.AssembleAsync(metadata, "baseline-1", "dynamic-1", "real-1");
        var package2 = await assembler.AssembleAsync(metadata, "base-2", "dyn-2", "real-2");

        Assert.NotNull(package1.Hints);
        Assert.NotNull(package2.Hints);
        Assert.NotEqual(package1.Hints.BreakpointAfterBaseline, package2.Hints.BreakpointAfterBaseline);
    }
}
