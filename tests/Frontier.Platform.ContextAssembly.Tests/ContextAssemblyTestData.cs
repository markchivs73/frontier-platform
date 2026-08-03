using Frontier.Platform.Serialization;
namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>Shared fixtures for ContextAssembly tests.</summary>
internal static class ContextAssemblyTestData
{
    public static ContextPackage Package(
        string baseline = "baseline content",
        string dynamic = "dynamic content",
        string realTime = "real-time content") =>
        new()
        {
            Baseline = new BaselineTier
            {
                BaselineVersion = "1.0",
                Components = new[] { "default" },
                Content = baseline
            },
            Dynamic = new DynamicTier
            {
                EngagementId = "eng-test",
                DynamicEpoch = 0,
                AssembledFromSnapshotRef = "snap-ref",
                Content = dynamic
            },
            RealTime = string.IsNullOrEmpty(realTime) ? null : new RealTimeTier
            {
                Fetches = new List<RealTimeFetch>(),
                Content = realTime
            },
            Hints = new CacheHint
            {
                BreakpointAfterBaseline = baseline.Length,
                BreakpointAfterDynamic = baseline.Length + dynamic.Length,
                BaselineCacheKey = CanonicalProfile.Hash(baseline),
                DynamicCacheKey = CanonicalProfile.Hash(dynamic)
            }
        };

    public static CachingMetadata Metadata(
        string providerId = "anthropic",
        string modelId = "claude-test",
        string? modelVersion = null,
        int maxTokens = 4096) =>
        new(
            ProviderId: providerId,
            ModelId: modelId,
            ModelVersion: modelVersion,
            MaxTokens: maxTokens,
            AssembledAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
}
