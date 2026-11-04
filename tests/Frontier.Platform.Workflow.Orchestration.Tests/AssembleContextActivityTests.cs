using Frontier.Platform.Serialization;
using Frontier.Platform.ContextAssembly;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Tests for <see cref="AssembleContextActivity"/> (S3.3 ADR-CR1).</summary>
public sealed class AssembleContextActivityTests
{
    private static CachingMetadata Metadata() => new(
        ProviderId: "anthropic",
        ModelId: "claude-test",
        ModelVersion: null,
        MaxTokens: 4096,
        AssembledAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private static ContextPackage Package()
    {
        var baselineTier = new BaselineTier { BaselineVersion = "1.0", Components = new[] { "default" }, Content = "baseline" };
        var dynamicTier = new DynamicTier { EngagementId = "eng-test", DynamicEpoch = 0, AssembledFromSnapshotRef = "snap-ref", Content = "dynamic" };
        var realTimeTier = new RealTimeTier { Fetches = new List<RealTimeFetch>(), Content = "real-time" };
        var hints = new CacheHint { BreakpointAfterBaseline = 8, BreakpointAfterDynamic = 15, BaselineCacheKey = "baseline", DynamicCacheKey = "dynamic" };
        return new ContextPackage { Baseline = baselineTier, Dynamic = dynamicTier, RealTime = realTimeTier, Hints = hints };
    }

    [Fact]
    public void Constructor_NullAssembler_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new AssembleContextActivity(null!));

    [Fact]
    public async Task RunAsync_NullRequest_Throws()
    {
        var activity = new AssembleContextActivity(new FakeContextAssembler(Package()));

        await Assert.ThrowsAsync<ArgumentNullException>(() => activity.RunAsync(null!));
    }

    [Fact]
    public async Task RunAsync_InvalidRequest_ThrowsFromValidate()
    {
        var activity = new AssembleContextActivity(new FakeContextAssembler(Package()));
        var request = new AssembleContextRequest(null!, "baseline", "dynamic", "real-time");

        await Assert.ThrowsAsync<ArgumentNullException>(() => activity.RunAsync(request));
    }

    [Fact]
    public async Task RunAsync_ValidRequest_DelegatesToAssemblerAndReturnsPackage()
    {
        var expected = Package();
        var assembler = new FakeContextAssembler(expected);
        var activity = new AssembleContextActivity(assembler);
        var request = new AssembleContextRequest(Metadata(), "baseline-content", "dynamic-content", "real-time-content");

        var result = await activity.RunAsync(request);

        Assert.Same(expected, result);
        Assert.Equal(Metadata(), assembler.ReceivedMetadata);
        Assert.Equal("baseline-content", assembler.ReceivedBaselineContent);
        Assert.Equal("dynamic-content", assembler.ReceivedDynamicContent);
        Assert.Equal("real-time-content", assembler.ReceivedRealTimeContent);
    }

    [Fact]
    public async Task RunAsync_RequestCitesBaseline_PackageCarriesItsReleaseAndComponents()
    {
        // S13.218 (doc 04 §4 step 2): the request's baseline provenance reaches the package.
        var assembler = new FakeContextAssembler(Package());
        var activity = new AssembleContextActivity(assembler);
        var provenance = new DynamicTierProvenance("eng-1", 2, "eng-1:ctx:e000002");
        var request = new AssembleContextRequest(Metadata(), "baseline-content", "dynamic-content", "real-time-content", provenance)
        {
            BaselineProvenance = new BaselineTierProvenance("2026-q2", ["firm-standards", "playbooks"]),
        };

        var result = await activity.RunAsync(request);

        Assert.Equal("2026-q2", result.Baseline.BaselineVersion);
        Assert.Equal(["firm-standards", "playbooks"], result.Baseline.Components);
        Assert.Equal(provenance, assembler.ReceivedDynamicProvenance);
    }

    [Fact]
    public async Task RunAsync_RequestCitesNoBaseline_ReturnsTheAssemblersPackageUnchanged()
    {
        var expected = Package();
        var activity = new AssembleContextActivity(new FakeContextAssembler(expected));

        var result = await activity.RunAsync(new AssembleContextRequest(Metadata(), "b", "d", "r"));

        Assert.Same(expected, result);
    }
}
