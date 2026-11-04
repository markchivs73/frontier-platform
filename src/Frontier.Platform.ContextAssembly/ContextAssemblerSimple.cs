using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Simple context assembler (S3.3 ADR-CR1) that takes pre-composed tier content
/// and applies provider-specific caching strategy directives. Does not fetch from stores;
/// that's a caller concern (agents or orchestrator).
/// </summary>
internal sealed class ContextAssemblerSimple : IContextAssembler
{
    /// <summary>The baseline version a package carries when the caller had no stored release to cite.</summary>
    internal const string UncitedBaselineVersion = "1.0";

    /// <summary>The component list a package carries when the caller had no stored release to cite.</summary>
    internal static readonly IReadOnlyList<string> UncitedBaselineComponents = ["default"];

    private readonly ICachingStrategyRegistry cachingStrategyRegistry;

    /// <summary>Constructs an assembler that resolves caching strategies from <paramref name="cachingStrategyRegistry"/>.</summary>
    public ContextAssemblerSimple(ICachingStrategyRegistry cachingStrategyRegistry)
    {
        ArgumentNullException.ThrowIfNull(cachingStrategyRegistry);
        this.cachingStrategyRegistry = cachingStrategyRegistry;
    }

    /// <inheritdoc />
    public Task<ContextPackage> AssembleAsync(
        CachingMetadata metadata,
        string baselineContent,
        string dynamicContent,
        string realTimeContent,
        DynamicTierProvenance? dynamicProvenance = null,
        CancellationToken ct = default) =>
        AssembleCoreAsync(metadata, baselineContent, dynamicContent, realTimeContent, null, dynamicProvenance, ct);

    /// <inheritdoc />
    public Task<ContextPackage> AssembleWithProvenanceAsync(
        CachingMetadata metadata,
        string baselineContent,
        string dynamicContent,
        string realTimeContent,
        BaselineTierProvenance baselineProvenance,
        DynamicTierProvenance? dynamicProvenance,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(baselineProvenance);
        return AssembleCoreAsync(metadata, baselineContent, dynamicContent, realTimeContent, baselineProvenance, dynamicProvenance, ct);
    }

    /// <summary>
    /// Builds the baseline tier (doc 04 §4 step 2): <c>BaselineTier(catalogue.Version, orderedNames, content)</c>.
    /// The uncited placeholders remain only for an assembly whose caller had no stored release to
    /// cite — the composer always supplies <paramref name="provenance"/> (S13.218).
    /// </summary>
    internal static BaselineTier BuildBaselineTier(string content, BaselineTierProvenance? provenance) => new()
    {
        BaselineVersion = provenance?.BaselineVersion ?? UncitedBaselineVersion,
        Components = provenance?.Components ?? UncitedBaselineComponents,
        Content = content,
    };

    /// <summary>
    /// Builds the dynamic tier (S13.60, doc 04 §4 step 3): the tier cites the epoch it was read
    /// from. The placeholders remain only for an assembly with nothing stored to cite — they are
    /// not a default, and a pinned read never arrives here without provenance.
    /// </summary>
    internal static DynamicTier BuildDynamicTier(string content, DynamicTierProvenance? provenance) => new()
    {
        EngagementId = provenance?.EngagementId.Value ?? "unknown",
        DynamicEpoch = provenance?.Epoch ?? 0,
        AssembledFromSnapshotRef = provenance?.AssembledFromSnapshotRef ?? "unknown",
        Content = content,
    };

    /// <summary>Builds the real-time tier, or <see langword="null"/> when there is no real-time content.</summary>
    internal static RealTimeTier? BuildRealTimeTier(string content) =>
        string.IsNullOrEmpty(content) ? null : new RealTimeTier { Fetches = new List<RealTimeFetch>(), Content = content };

    /// <summary>Builds the cache hints: breakpoints after the baseline and dynamic tiers, and each tier's content hash.</summary>
    internal static CacheHint BuildHints(string baselineContent, string dynamicContent) => new()
    {
        BreakpointAfterBaseline = baselineContent.Length,
        BreakpointAfterDynamic = baselineContent.Length + dynamicContent.Length,
        BaselineCacheKey = CanonicalProfile.Hash(baselineContent),
        DynamicCacheKey = CanonicalProfile.Hash(dynamicContent),
    };

    private async Task<ContextPackage> AssembleCoreAsync(
        CachingMetadata metadata,
        string baselineContent,
        string dynamicContent,
        string realTimeContent,
        BaselineTierProvenance? baselineProvenance,
        DynamicTierProvenance? dynamicProvenance,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(baselineContent);
        ArgumentNullException.ThrowIfNull(dynamicContent);
        ArgumentNullException.ThrowIfNull(realTimeContent);

        var package = new ContextPackage
        {
            Baseline = BuildBaselineTier(baselineContent, baselineProvenance),
            Dynamic = BuildDynamicTier(dynamicContent, dynamicProvenance),
            RealTime = BuildRealTimeTier(realTimeContent),
            Hints = BuildHints(baselineContent, dynamicContent),
        };

        var strategy = cachingStrategyRegistry.Resolve(metadata.ProviderId, metadata.ModelId, metadata.ModelVersion);
        if (strategy is not null)
        {
            await strategy.ApplyCacheHintsAsync(package, metadata, ct);
        }

        return package;
    }
}
