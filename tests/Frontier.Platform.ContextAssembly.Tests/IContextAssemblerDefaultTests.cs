using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>
/// Tests for <see cref="IContextAssembler.AssembleWithProvenanceAsync"/>'s default implementation
/// (S13.218): an implementer that predates it still stamps the cited baseline release.
/// </summary>
public sealed class IContextAssemblerDefaultTests
{
    [Fact]
    public async Task AssembleWithProvenanceAsync_DefaultImplementation_StampsBaselineAndKeepsTheRest()
    {
        var legacy = new LegacyAssembler();
        IContextAssembler assembler = legacy;
        var dynamic = new DynamicTierProvenance("eng-1", 2, "eng-1:ctx:e000002");

        var package = await assembler.AssembleWithProvenanceAsync(
            ContextAssemblyTestData.Metadata(), "baseline", "dynamic", "", new BaselineTierProvenance("2026-q2", ["firm-standards"]), dynamic, CancellationToken.None);

        Assert.Equal("2026-q2", package.Baseline.BaselineVersion);
        Assert.Equal(["firm-standards"], package.Baseline.Components);
        Assert.Equal("baseline", package.Baseline.Content);
        Assert.Equal(dynamic, legacy.ReceivedDynamicProvenance);
        Assert.Equal("eng-1", package.Dynamic.EngagementId.Value);
    }

    [Fact]
    public async Task AssembleWithProvenanceAsync_DefaultImplementation_NullBaselineProvenance_Throws()
    {
        IContextAssembler assembler = new LegacyAssembler();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            assembler.AssembleWithProvenanceAsync(ContextAssemblyTestData.Metadata(), "b", "d", "", null!, null, CancellationToken.None));
    }

    /// <summary>An implementer of only the original overload, as a consumer's assembler written before S13.218 would be.</summary>
    private sealed class LegacyAssembler : IContextAssembler
    {
        public DynamicTierProvenance? ReceivedDynamicProvenance { get; private set; }

        public Task<ContextPackage> AssembleAsync(
            CachingMetadata metadata,
            string baselineContent,
            string dynamicContent,
            string realTimeContent,
            DynamicTierProvenance? dynamicProvenance = null,
            CancellationToken ct = default)
        {
            ReceivedDynamicProvenance = dynamicProvenance;
            return Task.FromResult(new ContextPackage
            {
                Baseline = new BaselineTier { BaselineVersion = "legacy", Components = ["legacy"], Content = baselineContent },
                Dynamic = new DynamicTier { EngagementId = dynamicProvenance?.EngagementId.Value ?? "unknown", DynamicEpoch = 0, AssembledFromSnapshotRef = "ref", Content = dynamicContent },
                Hints = new CacheHint { BreakpointAfterBaseline = 0, BreakpointAfterDynamic = 0, BaselineCacheKey = "b", DynamicCacheKey = "d" },
            });
        }
    }
}
