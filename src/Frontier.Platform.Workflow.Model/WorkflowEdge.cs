using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// A directed edge between two <see cref="WorkflowNode"/>s (doc 00 §3.3). Data edges are
/// load-bearing: the cascade dependency graph is derived from typed data edges at
/// compile time, not maintained as separate configuration (ADR-3).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record WorkflowEdge
{
    /// <summary>The source node's <see cref="WorkflowNode.NodeId"/>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("from_node_id")]
    public required string FromNodeId { get; init; }

    /// <summary>The target node's <see cref="WorkflowNode.NodeId"/>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("to_node_id")]
    public required string ToNodeId { get; init; }

    /// <summary>Whether this edge carries control flow only, or a typed data dependency.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("kind")]
    public required EdgeKind Kind { get; init; }

    /// <summary>For <see cref="EdgeKind.Data"/> edges, the contract type produced by <see cref="FromNodeId"/> and consumed by <see cref="ToNodeId"/>.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("contract_type")]
    public string? ContractType { get; init; }

    /// <summary>For edges leaving a <see cref="DecisionNode"/>, the branch key this edge corresponds to.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("condition")]
    public string? Condition { get; init; }
}
