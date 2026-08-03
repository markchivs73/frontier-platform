using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>
/// S6.2 gate test (closeout): exercises all ContextAssembly features from S6.2a-c.
/// Assemble context → refresh with no change (no epoch bump) → refresh with change
/// (epoch bump) → compare shows diff → OpenAI strategy resolves.
/// </summary>
public sealed class ContextAssemblyS62GateTests
{
    [Fact]
    public async Task S62_AssembleRefreshCompareAndResolveOpenAi()
    {
        // === S6.2a: Assemble context ===
        var store = new Phase1EngagementContextStore();
        var engagementId = new EngagementId("eng-gate");
        var initialDynamic = """{"version":1,"data":"baseline"}""";
        var changedDynamic = """{"version":2,"data":"updated"}""";

        // Seed initial context
        await store.UpsertDynamicContextAsync(engagementId, initialDynamic, CancellationToken.None);

        // Simulate assembly (simplified: just read it)
        var assembledContent = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        Assert.Equal(initialDynamic, assembledContent);

        var initialPackage = new ContextPackage
        {
            Baseline = new BaselineTier
            {
                BaselineVersion = "2026.06.1",
                Components = new[] { "comp-a" },
                Content = """{"baseline":"data"}""",
            },
            Dynamic = new DynamicTier
            {
                EngagementId = engagementId,
                DynamicEpoch = 0,
                AssembledFromSnapshotRef = "snap-ref",
                Content = assembledContent!,
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

        // === S6.2b: Refresh with no change (epoch stays 0) ===
        using var refresher = new DynamicContextRefresher(store, new NoOpLogger());
        var result1 = await refresher.RefreshDynamicAsync(engagementId, initialDynamic, "no-change-test", CancellationToken.None);

        Assert.False(result1.Refreshed);
        Assert.Equal(0, result1.Epoch);
        Assert.Equal(CanonicalProfile.Hash(initialDynamic), result1.ContentHash);

        // === S6.2b: Refresh with change (epoch increments to 1) ===
        var result2 = await refresher.RefreshDynamicAsync(engagementId, changedDynamic, "changed-test", CancellationToken.None);

        Assert.True(result2.Refreshed);
        Assert.Equal(1, result2.Epoch);
        Assert.Equal(CanonicalProfile.Hash(changedDynamic), result2.ContentHash);

        // Verify content persisted
        var refreshedContent = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        Assert.Equal(changedDynamic, refreshedContent);

        // === S6.2c: Compare packages shows the diff ===
        var refreshedPackage = new ContextPackage
        {
            Baseline = new BaselineTier
            {
                BaselineVersion = "2026.06.1",
                Components = new[] { "comp-a" },
                Content = """{"baseline":"data"}""",
            },
            Dynamic = new DynamicTier
            {
                EngagementId = engagementId,
                DynamicEpoch = 1,
                AssembledFromSnapshotRef = "snap-ref",
                Content = changedDynamic,
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

        var debugger = new ContextDebugger();
        var comparison = await debugger.CompareAsync(refreshedPackage, initialPackage, CancellationToken.None);

        // Baseline unchanged
        Assert.False(comparison.BaselineComparison.ChangedFromPrevious);
        Assert.Equal(CanonicalProfile.Hash("""{"baseline":"data"}"""), comparison.BaselineComparison.ContentHash);

        // Dynamic changed
        Assert.True(comparison.DynamicComparison.ChangedFromPrevious);
        Assert.Equal(CanonicalProfile.Hash(changedDynamic), comparison.DynamicComparison.ContentHash);

        // === S6.2c: OpenAI strategy resolves ===
        var registry = new CachingStrategyRegistry(NoCachingStrategy.Instance);
        registry.Register("anthropic", "claude-*", versionPattern: null, new AnthropicCachingStrategy());
        registry.Register("openai", modelPattern: "*", versionPattern: null, new OpenAiCachingStrategy());

        var openAiStrategy = registry.Resolve("openai", "gpt-4", null);
        Assert.NotNull(openAiStrategy);
        Assert.Equal("openai", openAiStrategy.ProviderName);

        // Verify capabilities
        var capabilities = openAiStrategy.GetCapabilities();
        Assert.True(capabilities.SupportsImplicitPrefixCaching);
        Assert.Equal(1024, capabilities.MinTokensForCaching);
    }

    /// <summary>
    /// Minimal logger for testing.
    /// </summary>
    private sealed class NoOpLogger : ILogger<DynamicContextRefresher>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
