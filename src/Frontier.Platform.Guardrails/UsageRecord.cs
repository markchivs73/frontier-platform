using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// One invocation's actual usage (doc 07 §6), recorded by the invocation pipeline
/// (S4.2) after the MAF call from the model provider's reported usage.
/// <see cref="CorrelationId"/> is the idempotency key — <see cref="IBudgetLedger.RecordUsageAsync"/>
/// must not double-count a retried activity's usage.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by BudgetLedger tests.")]
public sealed record UsageRecord(
    string CorrelationId,
    string ExecutionId,
    string EngagementId,
    string NodeId,
    string AgentRole,
    string ResolvedModel,
    long InputTokens,
    long OutputTokens,
    decimal CostGbp);
