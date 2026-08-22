using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Base type for the eight node types a <see cref="WorkflowDefinition"/> can contain
/// (doc 00 §3.2). Polymorphic (de)serialization is handled natively by
/// <see cref="JsonPolymorphicAttribute"/>/<see cref="JsonDerivedTypeAttribute"/> — the
/// wire discriminator is <c>node_type</c>, with values matching <see cref="NodeType"/>'s
/// wire names. This keeps the converter entirely declarative and inside this assembly
/// (Abstractions stays at zero Frontier dependencies).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "node_type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(AgentTaskNode), "agent_task")]
[JsonDerivedType(typeof(HumanGateNode), "human_gate")]
[JsonDerivedType(typeof(DecisionNode), "decision")]
[JsonDerivedType(typeof(ParallelNode), "parallel")]
[JsonDerivedType(typeof(LoopNode), "loop")]
[JsonDerivedType(typeof(McpToolNode), "mcp_tool")]
#pragma warning disable CS0618 // ContextInjectionNode is deprecated (ADR-CR1) but remains a valid wire shape for backward compatibility.
[JsonDerivedType(typeof(ContextInjectionNode), "context_injection")]
#pragma warning restore CS0618
[JsonDerivedType(typeof(CascadeCheckNode), "cascade_check")]
public abstract record WorkflowNode
{
    /// <summary>Stable identifier for this node within its <see cref="WorkflowDefinition"/>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("node_id")]
    public required string NodeId { get; init; }

    /// <summary>The section this node produces output for, if any.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("artifact_key")]
    public string? ArtifactKey { get; init; }

    /// <summary>Resilience policy reference for this node's activity, if any (doc 09 §3).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("retry")]
    public RetryPolicySpec? Retry { get; init; }

    /// <summary>The node's discriminator value, for in-memory type checks. Not separately serialized — the wire discriminator is <c>node_type</c> on the JSON object.</summary>
    [JsonIgnore]
    public abstract NodeType NodeType { get; }
}
