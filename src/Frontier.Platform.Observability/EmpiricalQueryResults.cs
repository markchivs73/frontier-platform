using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Observability;

/// <summary>
/// Cache economics across all tiers for the given <see cref="EmpiricalScope"/> (doc 11 §2).
/// <see cref="TotalCostSaved"/> is in <see cref="Currency"/> (ISO 4217, ADR-PA21), <c>null</c> when there are no rows.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record TierEconomics(
    IReadOnlyList<TierEconomicsRow> Rows,
    decimal TotalCostSaved,
    string? Currency,
    int TotalExecutions);

/// <summary>Per-tier breakdown within a <see cref="TierEconomics"/> result; <see cref="CostSaved"/> is in <see cref="Currency"/> (ISO 4217).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record TierEconomicsRow(
    string Tier,
    decimal HitRate,
    long TokensTotal,
    long TokensCached,
    decimal CostSaved,
    string Currency);

/// <summary>Retry reason/model distribution for the given scope (doc 11 §4.2).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record RetryDistribution(
    IReadOnlyList<RetryDistributionRow> Rows,
    int TotalRetries,
    int TotalInvocations);

/// <summary>One reason-code/model bucket within a <see cref="RetryDistribution"/>.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record RetryDistributionRow(
    string ReasonCode,
    string Model,
    int Count,
    decimal RatePercent);

/// <summary>Validator outcome distribution (doc 11 §4.2).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record ValidatorDistribution(IReadOnlyList<ValidatorOutcomeRow> Rows);

/// <summary>One validator/status bucket within a <see cref="ValidatorDistribution"/>.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record ValidatorOutcomeRow(
    string ValidatorId,
    string Status,
    int Count,
    decimal RatePercent);

/// <summary>HITL gate evidence (doc 11 §4.2): decision distribution and time-to-decision.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record HitlEvidence(
    IReadOnlyList<HitlDecisionRow> Rows,
    decimal MeanTimeToDecisionMs);

/// <summary>One gate-kind/decision bucket within a <see cref="HitlEvidence"/> result.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record HitlDecisionRow(
    string GateKind,
    string Decision,
    int Count,
    decimal RatePercent);

/// <summary>Per-node measured reality for rendering on the design canvas (doc 11 §7).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record NodeMetricsOverlay(
    string WorkflowId,
    int DefinitionVersion,
    int ExecutionsInWindow,
    IReadOnlyDictionary<string, NodeMetrics> Nodes);

/// <summary>Aggregated metrics for a single canvas node (doc 11 §7); <see cref="MeanCost"/> is in <see cref="Currency"/> (ISO 4217, ADR-PA21).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record.")]
public sealed record NodeMetrics(
    decimal MeanCost,
    string Currency,
    long P50LatencyMs,
    long P95LatencyMs,
    decimal ValidatorPassRate,
    decimal MeanRetries,
    decimal? BaselineHitRate,
    decimal? DynamicHitRate,
    decimal? HitlRejectRate,
    MaturityBand? AgentMaturity);
