using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Simple context assembler (S3.3 ADR-CR1) that takes pre-composed tier content
/// and applies provider-specific caching strategy directives. Does not fetch from stores;
/// that's a caller concern (agents or orchestrator).
/// </summary>
internal sealed class ContextAssemblerSimple : IContextAssembler
{
    private readonly ICachingStrategyRegistry cachingStrategyRegistry;

    public ContextAssemblerSimple(ICachingStrategyRegistry cachingStrategyRegistry)
    {
        ArgumentNullException.ThrowIfNull(cachingStrategyRegistry);
        this.cachingStrategyRegistry = cachingStrategyRegistry;
    }

    /// <summary>
    /// Assembles a ContextPackage from three pre-composed tier content strings
    /// and applies provider-specific caching strategy directives.
    /// </summary>
    public async Task<ContextPackage> AssembleAsync(
        CachingMetadata metadata,
        string baselineContent,
        string dynamicContent,
        string realTimeContent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(baselineContent);
        ArgumentNullException.ThrowIfNull(dynamicContent);
        ArgumentNullException.ThrowIfNull(realTimeContent);

        // Compose the three-tier package with tier objects
        var assembledAt = DateTime.UtcNow;
        
        var baselineTier = new BaselineTier
        {
            BaselineVersion = "1.0",
            Components = new[] { "default" },
            Content = baselineContent
        };

        var dynamicTier = new DynamicTier
        {
            EngagementId = "unknown",
            DynamicEpoch = 0,
            AssembledFromSnapshotRef = "unknown",
            Content = dynamicContent
        };

        var realTimeTier = string.IsNullOrEmpty(realTimeContent) ? null : new RealTimeTier
        {
            Fetches = new List<RealTimeFetch>(),
            Content = realTimeContent
        };

        var hints = new CacheHint
        {
            BreakpointAfterBaseline = baselineContent.Length,
            BreakpointAfterDynamic = baselineContent.Length + dynamicContent.Length,
            BaselineCacheKey = CanonicalProfile.Hash(baselineContent),
            DynamicCacheKey = CanonicalProfile.Hash(dynamicContent)
        };

        var package = new ContextPackage
        {
            Baseline = baselineTier,
            Dynamic = dynamicTier,
            RealTime = realTimeTier,
            Hints = hints
        };

        // Resolve caching strategy and apply directives
        var strategy = cachingStrategyRegistry.Resolve(metadata.ProviderId, metadata.ModelId, metadata.ModelVersion);
        if (strategy == null)
        {
            return package;
        }

        var layout = await strategy.ApplyCacheHintsAsync(package, metadata, ct);

        return package;
    }
}
