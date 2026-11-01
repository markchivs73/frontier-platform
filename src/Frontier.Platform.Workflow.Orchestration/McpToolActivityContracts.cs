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
    [JsonPropertyOrder(0)]
    [JsonPropertyName("node_id")]
    public required string NodeId { get; init; }

    /// <summary>The section this node writes, if any.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("artifact_key")]
    public string? ArtifactKey { get; init; }

    /// <summary>The registered tool to call, <c>{reverse-dns-server}/{tool}</c> (ADR-CD9).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("tool_ref")]
    public required string ToolRef { get; init; }

    /// <summary>Per-invocation timeout in seconds (<see cref="Abstractions.McpToolNode.TimeoutSeconds"/>; <c>timeouts.nesting</c> validates it against the DTF cap).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("timeout_seconds")]
    public required int TimeoutSeconds { get; init; }

    /// <summary>
    /// The idempotency key for a write call (doc 00 §2.1, <c>mcp.write-idempotency</c>):
    /// minted orchestrator-side as the step's correlation id — deterministic per node
    /// occurrence, so an activity retry provably reuses the same key. Null for reads.
    /// </summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }

    /// <summary>Idempotency and audit join key for this invocation.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    /// <summary>The running execution's instance id (sandbox runs carry the <c>SANDBOX-</c> prefix, which write-fences the tool — doc 13 §5).</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>The engagement this execution runs for.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The upstream Data-edge payload (canonical JSON object), mapped onto the tool's arguments by wire name; null when the node has no Data-edge predecessor.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("input_payload")]
    public string? InputPayload { get; init; }

    /// <summary>
    /// The definition's pinned snapshot of the node's input contract (ADR-E27 D9, D15): the tool's
    /// arguments are validated against it before the call. Optional per the ADR-E15 floor:
    /// <see langword="null"/> when the node names none, and on inputs recorded before contracts were data.
    /// </summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("input_contract")]
    public Frontier.Platform.Abstractions.ContractSnapshot? InputContract { get; init; }

    /// <summary>The definition's pinned snapshot of the node's output contract (ADR-E27 D9, D15): the tool's result is validated against it; optional, as <see cref="InputContract"/>.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("output_contract")]
    public Frontier.Platform.Abstractions.ContractSnapshot? OutputContract { get; init; }

    /// <summary>
    /// The node's pinned write flag (<see cref="Abstractions.McpToolNode.Writes"/>, ADR-PA41), handed to
    /// <see cref="IMcpToolCatalog"/> on the tool's <see cref="McpToolPin"/> so the sandbox write fence
    /// follows it rather than the newest registry version's flag (ADR-PA42). <b>Additive and optional</b>
    /// per the ADR-E15 floor: <see langword="null"/> for a node that pins none, and on every input recorded
    /// before this field, whose tool is fenced as before.
    /// </summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("writes")]
    public bool? Writes { get; init; }
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

    /// <summary>
    /// The tool result as canonical JSON (a sandbox-fenced write returns the simulated ack, <c>{"simulated":true,...}</c>).
    /// When the node pins an output contract, it is the result's <c>structuredContent</c> alone, the value held to
    /// that contract (ADR-PA41).
    /// </summary>
    [JsonPropertyName("output_payload")]
    public required string OutputPayload { get; init; }

    /// <summary>SHA-256 hex of <see cref="OutputPayload"/>'s RFC 8785 (JCS) form, so the tool's key order does not move it (ADR-E2 decision 2).</summary>
    [JsonPropertyName("output_hash")]
    public required string OutputHash { get; init; }

    /// <summary>Whether the call was write-fenced to a simulated ack (sandbox test-run, doc 13 §5).</summary>
    [JsonPropertyName("simulated")]
    public required bool Simulated { get; init; }

    /// <summary>The host build that executed this call (ADR-E15/S13.17 pin-set stamp; activity-side, never orchestrator statics).</summary>
    [JsonPropertyName("host_build")]
    public string? HostBuild { get; init; }

    /// <summary>The hash of the registry contract that validated the arguments (ADR-E27 D15); <see langword="null"/> when there was none.</summary>
    [JsonPropertyName("input_contract_hash")]
    public string? InputContractHash { get; init; }

    /// <summary>The hash of the registry contract that validated the result (ADR-E27 D15); <see langword="null"/> when there was none, or the call was write-fenced.</summary>
    [JsonPropertyName("output_contract_hash")]
    public string? OutputContractHash { get; init; }

    /// <summary>
    /// SHA-256 hex of the RFC 8785 (JCS) form of the arguments exactly as the tool received them, the
    /// write's <c>idempotency_key</c> included (ADR-E2 decision 2, ADR-PA41); <see langword="null"/> on
    /// results recorded before the engine hashed them.
    /// </summary>
    [JsonPropertyName("input_hash")]
    public string? InputHash { get; init; }
}
