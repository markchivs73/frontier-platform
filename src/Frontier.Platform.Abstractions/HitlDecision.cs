using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// Records a human decision at a <c>HumanGateNode</c>, for inclusion in an
/// <c>ExecutionSnapshot</c> (doc 02 §2, doc 06 §3).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record HitlDecision
{
    /// <summary>The deciding gate's identifier (the <c>WorkflowNode.NodeId</c> of the <c>HumanGateNode</c>).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("gate_id")]
    public required string GateId { get; init; }

    /// <summary>Deterministic approval-request id: <c>{executionId}:{gateId}:{occurrence}</c> (doc 06 §3).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }

    /// <summary>The resolved human who decided.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("approver_id")]
    public required string ApproverId { get; init; }

    /// <summary>What was decided.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("kind")]
    public required DecisionKind Kind { get; init; }

    /// <summary>Free-text enrichment from the approver, if any.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>For a <see cref="DecisionKind.Reject"/>, the node to roll back to, if any.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("rollback_to_node_id")]
    public string? RollbackToNodeId { get; init; }

    /// <summary>UTC timestamp at which the decision was recorded.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("decided_at_utc")]
    public required DateTime DecidedAtUtc { get; init; }
}
