using Frontier.Platform.Serialization;
namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>Tests for the <see cref="OpenAiCachingStrategy"/> PoC strategy.</summary>
public sealed class OpenAiCachingStrategyTests
{
    private readonly OpenAiCachingStrategy strategy = new();

    [Fact]
    public void ProviderName_IsOpenAi() =>
        Assert.Equal("openai", strategy.ProviderName);

    [Fact]
    public void GetCapabilities_ReportsImplicitPrefixCachingSupport()
    {
        var capabilities = strategy.GetCapabilities();

        Assert.False(capabilities.SupportsExplicitDirectives);
        Assert.True(capabilities.SupportsImplicitPrefixCaching);
        Assert.Equal(1024, capabilities.MinTokensForCaching);
        Assert.Equal(["prefix-match"], capabilities.SupportedCacheDirectives);
    }

    [Fact]
    public async Task ApplyCacheHintsAsync_BelowCacheThreshold_AddsNoCacheDirective()
    {
        var package = ContextAssemblyTestData.Package(baseline: "short baseline", dynamic: "short dynamic");
        var metadata = ContextAssemblyTestData.Metadata();

        var layout = await strategy.ApplyCacheHintsAsync(package, metadata, CancellationToken.None);

        Assert.Empty(layout.CacheDirectives);
        Assert.Empty(layout.SystemMessages);
        Assert.Empty(layout.UserMessages);
        Assert.True(layout.EstimatedTokens > 0);
    }

    [Fact]
    public async Task ApplyCacheHintsAsync_AtOrAboveCacheThreshold_AddsPrefixMatchDirective()
    {
        var package = ContextAssemblyTestData.Package(
            baseline: new string('a', 2048),
            dynamic: new string('b', 2048));
        var metadata = ContextAssemblyTestData.Metadata();

        var layout = await strategy.ApplyCacheHintsAsync(package, metadata, CancellationToken.None);

        var directive = Assert.Single(layout.CacheDirectives);
        Assert.Equal("baseline+dynamic", directive.Tier);
        Assert.Equal("openai", directive.Provider);
        Assert.Equal("implicit", directive.Strategy);
        Assert.NotNull(directive.ExpiresAtUtc);
    }

    [Fact]
    public async Task ApplyCacheHintsAsync_NullRealTimeTier_EstimatesTokensWithoutThrowing()
    {
        // package.RealTime?.Content ?? "" — every other test leaves RealTime populated;
        // this exercises the null-tier fallback (S9.24 branch-coverage gap).
        var package = ContextAssemblyTestData.Package(realTime: "");
        var metadata = ContextAssemblyTestData.Metadata();

        var layout = await strategy.ApplyCacheHintsAsync(package, metadata, CancellationToken.None);

        Assert.Null(package.RealTime);
        Assert.True(layout.EstimatedTokens > 0);
    }

    [Fact]
    public void ExtractCacheMetrics_ReturnsNull() =>
        Assert.Null(strategy.ExtractCacheMetrics(providerResponse: "anything"));
}
