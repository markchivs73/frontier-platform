
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Projects staged <see cref="AuditTelemetryRecord"/>s into <see cref="AgentInvocation"/>s
/// (doc 05 §4 step 2): a direct field-for-field copy, dropping the three per-tier
/// cache-changed booleans that <see cref="CacheMetricsAggregator"/> consumes instead (C-15).
/// </summary>
internal static class AgentInvocationProjector
{
    /// <summary>Maps every staged record to its <see cref="AgentInvocation"/> projection.</summary>
    internal static IReadOnlyList<AgentInvocation> Project(IReadOnlyList<AuditTelemetryRecord> records) =>
        records.Select(ToAgentInvocation).ToArray();

    /// <summary>Maps one staged record to an <see cref="AgentInvocation"/>.</summary>
    internal static AgentInvocation ToAgentInvocation(AuditTelemetryRecord record) => new()
    {
        CorrelationId = record.CorrelationId,
        NodeId = record.NodeId,
        ArtifactKey = record.ArtifactKey,
        AgentRole = record.AgentRole,
        ResolvedModel = record.ResolvedModel,
        InputContractType = record.InputContractType,
        InputHash = record.InputHash,
        OutputContractType = record.OutputContractType,
        OutputHash = record.OutputHash,
        InputTokens = record.InputTokens,
        OutputTokens = record.OutputTokens,
        CacheReadTokens = record.CacheReadTokens,
        CacheWriteTokens = record.CacheWriteTokens,
        RetryCount = record.RetryCount,
        LatencyMs = record.LatencyMs,
        ToolCalls = record.ToolCalls,
        InvokedAtUtc = record.InvokedAtUtc,
    };
}
