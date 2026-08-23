using Frontier.Platform.ContextAssembly;
using ContextPackageContract = Frontier.Platform.Serialization.ContextPackage;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.2 tests for <see cref="PromptBuilder"/>.</summary>
public sealed class PromptBuilderTests
{
    [Fact]
    public void Build_ValidInputs_ComposesContextTiersAndInputPayload()
    {
        var prompt = PromptBuilder.Build(Package(), "SummaryArtifact", """{"narrative":"hello"}""");

        Assert.Contains("## Baseline\nbaseline", prompt, StringComparison.Ordinal);
        Assert.Contains("## Dynamic\ndynamic", prompt, StringComparison.Ordinal);
        Assert.Contains("## Real-time\nreal-time", prompt, StringComparison.Ordinal);
        Assert.Contains("# Input (SummaryArtifact)\n{\"narrative\":\"hello\"}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NullContext_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PromptBuilder.Build(null!, "SummaryArtifact", "{}"));
    }

    [Fact]
    public void Build_WhitespaceInputContractType_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PromptBuilder.Build(Package(), " ", "{}"));
    }

    [Fact]
    public void Build_NullInputPayloadJson_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PromptBuilder.Build(Package(), "SummaryArtifact", null!));
    }

    private static ContextPackageContract Package()
    {
        var baselineTier = new BaselineTier
        {
            BaselineVersion = "1.0",
            Components = new[] { "default" },
            Content = "baseline"
        };
        var dynamicTier = new DynamicTier
        {
            EngagementId = "eng-test",
            DynamicEpoch = 0,
            AssembledFromSnapshotRef = "snap-ref",
            Content = "dynamic"
        };
        var realTimeTier = new RealTimeTier
        {
            Fetches = new List<RealTimeFetch>(),
            Content = "real-time"
        };
        var hints = new CacheHint
        {
            BreakpointAfterBaseline = 8,
            BreakpointAfterDynamic = 15,
            BaselineCacheKey = "baseline",
            DynamicCacheKey = "dynamic"
        };
        return new ContextPackageContract
        {
            Baseline = baselineTier,
            Dynamic = dynamicTier,
            RealTime = realTimeTier,
            Hints = hints
        };
    }
}
