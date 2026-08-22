using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Bounded iteration over a body node (doc 00 §3.2). <see cref="MaxIterations"/> comes
/// from the definition, so the bound is deterministic across replay; a high value is
/// flagged by Guardrails as a runaway-loop signal (doc 07 §<c>MaxAgentInvocations</c>).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record LoopNode : WorkflowNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override NodeType NodeType => NodeType.Loop;

    /// <summary>The first node of the loop body.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("body_node_id")]
    public required string BodyNodeId { get; init; }

    /// <summary>The maximum number of iterations the interpreter will execute.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("max_iterations")]
    public required int MaxIterations { get; init; }
}
