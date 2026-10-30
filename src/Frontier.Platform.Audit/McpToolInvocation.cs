using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// One <c>mcp_tool</c> node's deterministic tool call (ADR-CD9), as the audit records it: which tool
/// the step called, the hashes of what went in and came out, and the hash of each registry contract
/// that validated it (ADR-E27 D15, ADR-PA41). Distinct from <see cref="ToolCall"/>, which is a call an
/// agent made during an <see cref="AgentInvocation"/>. Consolidated from the execution's completed
/// MCP steps; hashes, never payloads (doc 05 §6).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record McpToolInvocation
{
    /// <summary>Join key shared with the step's <see cref="WorkflowEvent.CorrelationId"/> (doc 05 §4 step 3).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    /// <summary>The <c>mcp_tool</c> node's identifier.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("node_id")]
    public required string NodeId { get; init; }

    /// <summary>The artifact this step produced output for, if any.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("artifact_key")]
    public string? ArtifactKey { get; init; }

    /// <summary>The registered tool the step called, <c>{reverse-dns-server}/{tool}</c> (ADR-CD9).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("tool_ref")]
    public required string ToolRef { get; init; }

    /// <summary>The registry ref of the node's input contract (ADR-PA39); <see langword="null"/> when the node names none.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("input_contract_type")]
    public string? InputContractType { get; init; }

    /// <summary>
    /// SHA-256 hex of the RFC 8785 (JCS) form of the arguments exactly as the tool received them, the
    /// write's <c>idempotency_key</c> included (ADR-E2 decision 2); <see langword="null"/> for a step
    /// recorded before the engine hashed them.
    /// </summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("input_hash")]
    public string? InputHash { get; init; }

    /// <summary>The registry ref of the node's output contract (ADR-PA39); <see langword="null"/> when the node names none.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("output_contract_type")]
    public string? OutputContractType { get; init; }

    /// <summary>SHA-256 hex of the recorded output's RFC 8785 (JCS) form (ADR-E2 decision 2), as on the step's completion.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("output_hash")]
    public required string OutputHash { get; init; }

    /// <summary>The hash of the registry contract that validated the arguments (ADR-E27 D15); <see langword="null"/> when none did.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("input_contract_hash")]
    public string? InputContractHash { get; init; }

    /// <summary>The hash of the registry contract that validated the result (ADR-E27 D15); <see langword="null"/> when none did, as for a write-fenced call.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("output_contract_hash")]
    public string? OutputContractHash { get; init; }

    /// <summary>
    /// <c>true</c> when the call was a sandbox write short-circuited to a simulated ack rather than
    /// reaching the connector (doc 13 §5), as <see cref="ToolCall.Simulated"/>; <see langword="null"/>
    /// (omitted on the wire) for every real call.
    /// </summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("simulated")]
    public bool? Simulated { get; init; }

    /// <summary>UTC timestamp at which the step completed.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("completed_at_utc")]
    public required DateTime CompletedAtUtc { get; init; }
}
