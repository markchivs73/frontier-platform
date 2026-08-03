using Frontier.Platform.Abstractions;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>
/// Basic smoke tests for context assembly and caching strategies (S3.2, S3.6 PoC gate).
/// </summary>
public sealed class ContextAssemblyTests
{
    [Fact]
    public void ContextPackage_CreatesWithTiers_RoundTrips()
    {
        // Arrange: create a minimal context package with tier objects
        var baselineTier = new BaselineTier
        {
            BaselineVersion = "1.0",
            Components = new[] { "default" },
            Content = "baseline content"
        };

        var dynamicTier = new DynamicTier
        {
            EngagementId = "eng-123",
            DynamicEpoch = 0,
            AssembledFromSnapshotRef = "snap-ref",
            Content = "dynamic content"
        };

        var realTimeTier = new RealTimeTier
        {
            Fetches = new List<RealTimeFetch>(),
            Content = "real-time content"
        };

        var hints = new CacheHint
        {
            BreakpointAfterBaseline = 18,
            BreakpointAfterDynamic = 36,
            BaselineCacheKey = "base-key",
            DynamicCacheKey = "dyn-key"
        };

        var package = new ContextPackage
        {
            Baseline = baselineTier,
            Dynamic = dynamicTier,
            RealTime = realTimeTier,
            Hints = hints
        };

        // Act & Assert
        Assert.NotNull(package);
        Assert.Equal("1.0", package.Baseline.BaselineVersion);
        Assert.Equal("eng-123", (string)package.Dynamic.EngagementId);
        Assert.Equal(18, package.Hints.BreakpointAfterBaseline);
    }

    [Fact]
    public void ContextPackageMetadata_Validates_NegativeBytes()
    {
        // Arrange
        var metadata = new ContextPackageMetadata(
            AssembledAtUtc: DateTime.UtcNow,
            BaselineBytes: -1,  // Invalid
            DynamicBytes: 0,
            RealTimeBytes: 0);

        // Act & Assert
        Assert.Throws<ContractViolationException>(() => metadata.Validate());
    }
}
