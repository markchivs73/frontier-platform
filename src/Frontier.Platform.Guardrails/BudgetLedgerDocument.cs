using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// Cosmos document shape for the <c>guardrail-ledger</c> container (doc 07 §6, S6.5a):
/// one doc per engagement, accumulating usage across all executions/invocations.
/// Stored as `{engagementId}:ledger` under PK `/engagement_id`; replaced whole under ETag optimistic
/// concurrency, with a bounded retry when a concurrent write wins the race.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised indirectly through BudgetLedger and CosmosBudgetLedger tests.")]
public sealed record BudgetLedgerDocument
{
    /// <summary>Partition key: `{engagementId}` (doc 02 §3 convention).</summary>
    [JsonPropertyName("partitionKey")]
    [JsonPropertyOrder(0)]
    public required string PartitionKey { get; init; }

    /// <summary>Document ID: `{engagementId}:ledger` (unique per engagement).</summary>
    [JsonPropertyName("id")]
    [JsonPropertyOrder(1)]
    public required string Id { get; init; }

    /// <summary>Engagement this ledger tracks.</summary>
    [JsonPropertyName("engagement_id")]
    [JsonPropertyOrder(2)]
    public required string EngagementId { get; init; }

    /// <summary>Cumulative input tokens consumed in this engagement.</summary>
    [JsonPropertyName("total_input_tokens")]
    [JsonPropertyOrder(3)]
    public long TotalInputTokens { get; init; }

    /// <summary>Cumulative output tokens consumed in this engagement.</summary>
    [JsonPropertyName("total_output_tokens")]
    [JsonPropertyOrder(4)]
    public long TotalOutputTokens { get; init; }

    /// <summary>
    /// Cumulative cost in <see cref="Currency"/>, a string decimal at scale 4 (doc 01 §3.3). Scale 4, not a
    /// budget's scale 2: one invocation costs a fraction of a cent (e.g. 0.0012), and scale 2 would round it away.
    /// </summary>
    [JsonPropertyName("total_cost")]
    [JsonPropertyOrder(5)]
    [DecimalPrecision(4)]
    public decimal TotalCost { get; init; }

    /// <summary>ISO 4217 code of <see cref="TotalCost"/>; usage in any other currency is refused (ADR-PA21).</summary>
    [JsonPropertyName("currency")]
    [JsonPropertyOrder(6)]
    public required string Currency { get; init; }

    /// <summary>Count of recorded usage events (invocations) in this engagement.</summary>
    [JsonPropertyName("invocation_count")]
    [JsonPropertyOrder(7)]
    public int InvocationCount { get; init; }

    /// <summary>Per-execution snapshot (id → latest tokens+cost), for hierarchical budget checks.</summary>
    [JsonPropertyName("execution_snapshots")]
    [JsonPropertyOrder(8)]
    public Dictionary<string, ExecutionLedgerSnapshot>? ExecutionSnapshots { get; init; }

    /// <summary>Cosmos metadata: ETag for optimistic concurrency on patches.</summary>
    [JsonPropertyName("_etag")]
    [JsonPropertyOrder(9)]
    public string? ETag { get; init; }

    /// <summary>Cosmos metadata: last-write timestamp.</summary>
    [JsonPropertyName("_ts")]
    [JsonPropertyOrder(10)]
    public long? Timestamp { get; init; }
}

/// <summary>
/// Snapshot of per-execution usage, stored inline in <see cref="BudgetLedgerDocument.ExecutionSnapshots"/>.
/// Allows hierarchical budget checks (engagement total vs. per-execution breakdown) without separate queries.
/// <c>total_cost</c> is a string decimal at scale 4, like <see cref="BudgetLedgerDocument.TotalCost"/>: per-invocation
/// costs are fractions of a cent, which a budget's scale 2 would round away.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; tested indirectly through BudgetLedger tests.")]
public sealed record ExecutionLedgerSnapshot(
    [property: JsonPropertyName("execution_id")] string ExecutionId,
    [property: JsonPropertyName("total_tokens")] long TotalTokens,
    [property: JsonPropertyName("total_cost"), DecimalPrecision(4)] decimal TotalCost,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("invocation_count")] int InvocationCount,
    [property: JsonPropertyName("last_updated_utc")] DateTime LastUpdatedUtc);
