using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Fan-out/fan-in over independent branches (doc 00 §3.2). Compiles to
/// <c>Task.WhenAll</c> over activities/sub-orchestrations; every branch must converge at
/// <see cref="JoinNodeId"/> (doc 13 §4.2 <c>graph.fan-out-fan-in</c>).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record ParallelNode : WorkflowNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override NodeType NodeType => NodeType.Parallel;

    /// <summary>The first node of each independent branch.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("branch_node_ids")]
    public required IReadOnlyList<string> BranchNodeIds { get; init; }

    /// <summary>The node at which every branch must converge.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("join_node_id")]
    public required string JoinNodeId { get; init; }
}
