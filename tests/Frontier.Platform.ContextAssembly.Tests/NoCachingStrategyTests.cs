using Frontier.Platform.Serialization;
namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>Tests for the <see cref="NoCachingStrategy"/> fallback strategy.</summary>
public sealed class NoCachingStrategyTests
{
    private readonly NoCachingStrategy strategy = NoCachingStrategy.Instance;

    [Fact]
    public void ProviderName_IsNone() =>
        Assert.Equal("none", strategy.ProviderName);

    [Fact]
    public void GetCapabilities_ReportsNoCachingSupport()
    {
        var capabilities = strategy.GetCapabilities();

        Assert.False(capabilities.SupportsExplicitDirectives);
        Assert.False(capabilities.SupportsImplicitPrefixCaching);
        Assert.Null(capabilities.MinTokensForCaching);
        Assert.Empty(capabilities.SupportedCacheDirectives);
    }

    [Fact]
    public async Task ApplyCacheHintsAsync_ReturnsEmptyLayout()
    {
        var package = ContextAssemblyTestData.Package();
        var metadata = ContextAssemblyTestData.Metadata();

        var layout = await strategy.ApplyCacheHintsAsync(package, metadata, CancellationToken.None);

        Assert.Empty(layout.SystemMessages);
        Assert.Empty(layout.UserMessages);
        Assert.Empty(layout.CacheDirectives);
        Assert.Equal(0, layout.EstimatedTokens);
    }

    [Fact]
    public void ExtractCacheMetrics_ReturnsNull() =>
        Assert.Null(strategy.ExtractCacheMetrics(providerResponse: null));
}
