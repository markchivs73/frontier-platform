using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// External system call via an MCP connector (doc 00 §3.2). Compiles to
/// <c>InvokeMcpToolActivity</c>; in the definition compiler's dry-run mode, writes either
/// execute against the connector's dry-run path or short-circuit with a simulated ack
/// (doc 13 §4.2).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record McpToolNode : WorkflowNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override NodeType NodeType => NodeType.McpTool;

    /// <summary>Reference to the registered connector tool to invoke (doc 13 §4.2 <c>mcp.tool-resolves</c>).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("tool_ref")]
    public required string ToolRef { get; init; }

    /// <summary>Per-invocation timeout, in seconds.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("timeout_seconds")]
    public required int TimeoutSeconds { get; init; }

    /// <summary>Specification for deriving this call's idempotency key, required for write operations.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("idempotency_key_spec")]
    public required string IdempotencyKeySpec { get; init; }
}
