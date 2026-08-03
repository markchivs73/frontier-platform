using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;

namespace Frontier.TestSupport;

/// <summary>
/// Shared telemetry-record sample (S11.6): used by Audit.Tests (staging-document tests)
/// and Orchestration.Tests (the relocated projector suite).
/// </summary>
internal static class TelemetrySamples
{
    /// <summary>A well-formed <see cref="AuditTelemetryRecord"/> (mirrors the S5.1 contract sample, doc 05 §9).</summary>
    public static AuditTelemetryRecord Record() => new()
    {
        ExecutionId = "eng-1::wf-chain",
        CorrelationId = "corr-3",
        NodeId = "pricing-agent",
        SectionKey = "pricing",
        AgentRole = "deep-reasoning",
        ResolvedModel = new ResolvedModelSummary
        {
            RoleId = "deep-reasoning",
            Provider = "anthropic",
            ModelId = "claude-fable-5",
            ModelVersion = "2026-01-01",
            ChainPosition = 0,
            MappingVersion = 1,
        },
        InputContractType = "EngagementBriefSection",
        InputHash = "a1b2c3",
        OutputContractType = "PricingSection",
        OutputHash = "d4e5f6",
        InputTokens = 1200,
        OutputTokens = 450,
        CacheReadTokens = 800,
        CacheWriteTokens = 200,
        RetryCount = 0,
        LatencyMs = 2400,
        ToolCalls = [],
        BaselineCacheChanged = false,
        DynamicCacheChanged = true,
        RealTimeCacheChanged = false,
        InvokedAtUtc = new DateTime(2026, 1, 1, 0, 20, 0, DateTimeKind.Utc),
    };
}
