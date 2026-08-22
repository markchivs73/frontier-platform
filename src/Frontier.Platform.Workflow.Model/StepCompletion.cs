using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Records that a <see cref="WorkflowNode"/> finished executing, for inclusion in an
/// <see cref="ExecutionSnapshot"/> (doc 02 §2). The full output payload is stored once
/// by reference in the artifact-state container; this record carries only its hash for
/// idempotency and audit join.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record StepCompletion
{
    /// <summary>The completed node's <see cref="WorkflowNode.NodeId"/>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("node_id")]
    public required string NodeId { get; init; }

    /// <summary>The completed node's <see cref="NodeType"/>, as its wire name.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("node_type")]
    public required NodeType NodeType { get; init; }

    /// <summary>The section this step produced output for, if any.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("artifact_key")]
    public string? ArtifactKey { get; init; }

    /// <summary>Idempotency and audit join key for this step's activity invocation.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    /// <summary>The wire type name of the step's output contract.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("output_contract_type")]
    public required string OutputContractType { get; init; }

    /// <summary>SHA256 hex hash of the output's canonical bytes; the payload itself is stored by reference.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("output_hash")]
    public required string OutputHash { get; init; }

    /// <summary>How many retry attempts the activity needed before this completion.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("retry_count")]
    public required int RetryCount { get; init; }

    /// <summary>UTC timestamp at which the step completed.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("completed_at_utc")]
    public required DateTime CompletedAtUtc { get; init; }

    /// <summary>
    /// The Model-Role Config resolution that served this step (doc 08 §6), or
    /// <c>null</c> for node types that don't invoke a model. Additive (S4.2): omitted
    /// on the wire for any <see cref="StepCompletion"/> recorded before this field
    /// existed, so existing golden-file bytes are unaffected (canonical-serialization
    /// skill — omit-null).
    /// </summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("resolved_model")]
    public ResolvedModelSummary? ResolvedModel { get; init; }

    /// <summary>
    /// The host build that executed this step's activity (ADR-E15 D1/D3 pin set, S13.17):
    /// "which code ran this step" as a recorded fact. Additive — omitted on the wire for
    /// steps recorded before this field existed, so existing golden-file bytes are
    /// unaffected (canonical-serialization skill — omit-null). Sourced from the recorded
    /// activity result, never from orchestrator-side statics (replay safety).
    /// </summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("host_build")]
    public string? HostBuild { get; init; }

    /// <summary>
    /// For a <see cref="Abstractions.NodeType.Decision"/> step: the branch target the
    /// predicate evaluation selected (ADR-5 Decision 6, S13.7j) — the routing fact,
    /// recorded for audit. Null for every other node type. Additive (omit-null): absent
    /// on records written before this field existed.
    /// </summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("selected_branch_node_id")]
    public string? SelectedBranchNodeId { get; init; }
}
