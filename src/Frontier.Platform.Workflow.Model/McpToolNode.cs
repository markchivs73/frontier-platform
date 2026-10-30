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

    /// <summary>
    /// The ADR-E2 ref of the registry contract the tool's arguments must satisfy, copied from the
    /// tool's registry record (ADR-E27 D9). The engine validates the call's input against it before
    /// the tool is invoked. Omitted on definitions published before contracts were data, which run
    /// unvalidated as they did.
    /// </summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("input_contract_type")]
    public string? InputContractType { get; init; }

    /// <summary>
    /// The ADR-E2 ref of the registry contract the tool's result must satisfy, copied from the tool's
    /// registry record (ADR-E27 D9). The engine validates the result against it before it is recorded.
    /// </summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("output_contract_type")]
    public string? OutputContractType { get; init; }

    /// <summary>
    /// Whether the tool mutates its connector's state, copied from the tool's registry record when
    /// the node is designed (ADR-PA41; consumer ADR-E27 D11) and held to it by
    /// <c>mcp.tool-resolves</c>. The engine runs a write with an idempotency key and the
    /// <c>mcp-write</c> profile, and a read with neither. Omitted on definitions published before
    /// the flag was pinned; for those the engine asks the deployment's write classifier, as it did.
    /// </summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("writes")]
    public bool? Writes { get; init; }
}
