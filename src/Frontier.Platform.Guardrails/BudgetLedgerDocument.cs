using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// Cosmos document shape for the <c>guardrail-ledger</c> container (doc 07 §6, S6.5a):
/// one doc per engagement, accumulating usage across all executions/invocations.
/// Stored as `{engagementId}:ledger` under PK `/engagementId`; updated via partial-document
/// patches (increment operations) for optimistic concurrency on high-contention scenarios.
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

    /// <summary>Cumulative cost in GBP (scale 2: string decimal per canonical profile).</summary>
    [JsonPropertyName("total_cost_gbp")]
    [JsonPropertyOrder(5)]
    public decimal TotalCostGbp { get; init; }

    /// <summary>Count of recorded usage events (invocations) in this engagement.</summary>
    [JsonPropertyName("invocation_count")]
    [JsonPropertyOrder(6)]
    public int InvocationCount { get; init; }

    /// <summary>Per-execution snapshot (id → latest tokens+cost), for hierarchical budget checks.</summary>
    [JsonPropertyName("execution_snapshots")]
    [JsonPropertyOrder(7)]
    public Dictionary<string, ExecutionLedgerSnapshot>? ExecutionSnapshots { get; init; }

    /// <summary>Cosmos metadata: ETag for optimistic concurrency on patches.</summary>
    [JsonPropertyName("_etag")]
    [JsonPropertyOrder(8)]
    public string? ETag { get; init; }

    /// <summary>Cosmos metadata: last-write timestamp.</summary>
    [JsonPropertyName("_ts")]
    [JsonPropertyOrder(9)]
    public long? Timestamp { get; init; }
}

/// <summary>
/// Snapshot of per-execution usage, stored inline in <see cref="BudgetLedgerDocument.ExecutionSnapshots"/>.
/// Allows hierarchical budget checks (engagement total vs. per-execution breakdown) without separate queries.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; tested indirectly through BudgetLedger tests.")]
public sealed record ExecutionLedgerSnapshot(
    [property: JsonPropertyName("execution_id")] string ExecutionId,
    [property: JsonPropertyName("total_tokens")] long TotalTokens,
    [property: JsonPropertyName("total_cost_gbp")] decimal TotalCostGbp,
    [property: JsonPropertyName("invocation_count")] int InvocationCount,
    [property: JsonPropertyName("last_updated_utc")] DateTime LastUpdatedUtc);
