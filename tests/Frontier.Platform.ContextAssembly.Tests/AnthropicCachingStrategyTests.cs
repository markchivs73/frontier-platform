using Frontier.Platform.Serialization;
namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>Tests for the <see cref="AnthropicCachingStrategy"/> PoC strategy.</summary>
public sealed class AnthropicCachingStrategyTests
{
    private readonly AnthropicCachingStrategy strategy = new();

    [Fact]
    public void ProviderName_IsAnthropic() =>
        Assert.Equal("anthropic", strategy.ProviderName);

    [Fact]
    public void GetCapabilities_ReportsExplicitDirectiveSupport()
    {
        var capabilities = strategy.GetCapabilities();

        Assert.True(capabilities.SupportsExplicitDirectives);
        Assert.False(capabilities.SupportsImplicitPrefixCaching);
        Assert.Equal(1024, capabilities.MinTokensForCaching);
        Assert.Equal(["ephemeral"], capabilities.SupportedCacheDirectives);
    }

    [Fact]
    public async Task ApplyCacheHintsAsync_WithNonEmptyBaseline_AddsEphemeralCacheDirective()
    {
        var package = ContextAssemblyTestData.Package(baseline: "baseline content");
        var metadata = ContextAssemblyTestData.Metadata();

        var layout = await strategy.ApplyCacheHintsAsync(package, metadata, CancellationToken.None);

        var directive = Assert.Single(layout.CacheDirectives);
        Assert.Equal("baseline", directive.Tier);
        Assert.Equal("anthropic", directive.Provider);
        Assert.Equal("explicit", directive.Strategy);
        Assert.NotNull(directive.ExpiresAtUtc);
        Assert.Empty(layout.SystemMessages);
        Assert.Empty(layout.UserMessages);
        Assert.True(layout.EstimatedTokens > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ApplyCacheHintsAsync_WithEmptyBaseline_AddsNoCacheDirective(string baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        var package = ContextAssemblyTestData.Package(baseline: baseline);
        var metadata = ContextAssemblyTestData.Metadata();

        var layout = await strategy.ApplyCacheHintsAsync(package, metadata, CancellationToken.None);

        Assert.Empty(layout.CacheDirectives);
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
