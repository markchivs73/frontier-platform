using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Input to <c>InvokeMcpToolActivity</c> (S13.7c, doc 00 §3.2 / ADR-CD9): one deterministic
/// MCP tool call for an <see cref="Abstractions.McpToolNode"/> — no agent, no model cost.
/// Payloads ride inline as canonical JSON (screen-size per the ADR-E1 tonnage rule; grant
/// minting for staged refs arrives with the TD-14 staging deployment — the activity never
/// dereferences a <c>payload_ref</c> and passes such envelopes through untouched).
/// </summary>
public sealed record McpToolActivityInput
{
    /// <summary>The invoking node's id.</summary>
    [JsonPropertyName("node_id")]
    public required string NodeId { get; init; }

    /// <summary>The section this node writes, if any.</summary>
    [JsonPropertyName("artifact_key")]
    public string? ArtifactKey { get; init; }

    /// <summary>The registered tool to call, <c>{reverse-dns-server}/{tool}</c> (ADR-CD9).</summary>
    [JsonPropertyName("tool_ref")]
    public required string ToolRef { get; init; }

    /// <summary>Per-invocation timeout in seconds (<see cref="Abstractions.McpToolNode.TimeoutSeconds"/>; <c>timeouts.nesting</c> validates it against the DTF cap).</summary>
    [JsonPropertyName("timeout_seconds")]
    public required int TimeoutSeconds { get; init; }

    /// <summary>
    /// The idempotency key for a write call (doc 00 §2.1, <c>mcp.write-idempotency</c>):
    /// minted orchestrator-side as the step's correlation id — deterministic per node
    /// occurrence, so an activity retry provably reuses the same key. Null for reads.
    /// </summary>
    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }

    /// <summary>Idempotency and audit join key for this invocation.</summary>
    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    /// <summary>The running execution's instance id (sandbox runs carry the <c>SANDBOX-</c> prefix, which write-fences the tool — doc 13 §5).</summary>
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>The engagement this execution runs for.</summary>
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The upstream Data-edge payload (canonical JSON object), mapped onto the tool's arguments by wire name; null when the node has no Data-edge predecessor.</summary>
    [JsonPropertyName("input_payload")]
    public string? InputPayload { get; init; }
}

/// <summary>Result of <c>InvokeMcpToolActivity</c>: the tool's response as canonical JSON, hashed for the step record.</summary>
public sealed record McpToolActivityResult
{
    /// <summary>The invoking node's id.</summary>
    [JsonPropertyName("node_id")]
    public required string NodeId { get; init; }

    /// <summary>The section this node writes, if any.</summary>
    [JsonPropertyName("artifact_key")]
    public string? ArtifactKey { get; init; }

    /// <summary>The tool that was called.</summary>
    [JsonPropertyName("tool_ref")]
    public required string ToolRef { get; init; }

    /// <summary>The tool result as canonical JSON (a sandbox-fenced write returns the simulated ack, <c>{"simulated":true,...}</c>).</summary>
    [JsonPropertyName("output_payload")]
    public required string OutputPayload { get; init; }

    /// <summary>SHA-256 hex of <see cref="OutputPayload"/>'s canonical bytes.</summary>
    [JsonPropertyName("output_hash")]
    public required string OutputHash { get; init; }

    /// <summary>Whether the call was write-fenced to a simulated ack (sandbox test-run, doc 13 §5).</summary>
    [JsonPropertyName("simulated")]
    public required bool Simulated { get; init; }

    /// <summary>The host build that executed this call (ADR-E15/S13.17 pin-set stamp; activity-side, never orchestrator statics).</summary>
    [JsonPropertyName("host_build")]
    public string? HostBuild { get; init; }
}
