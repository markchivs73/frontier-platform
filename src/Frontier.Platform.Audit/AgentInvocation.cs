using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// One agent invocation's audit-relevant facts (doc 05 §3), consolidated from the
/// invocation pipeline's staged <see cref="AuditTelemetryRecord"/>. <see cref="ResolvedModel"/>
/// uses <see cref="ResolvedModelSummary"/> (the richer audit-facing projection already
/// produced by S4.2/doc 08) rather than doc 05's bare model-id string.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record AgentInvocation
{
    /// <summary>Join key shared with the activity's <see cref="WorkflowEvent.CorrelationId"/> (doc 05 §4 step 3).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    /// <summary>The invoking node's identifier.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("node_id")]
    public required string NodeId { get; init; }

    /// <summary>The section this invocation produced output for, if any.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("section_key")]
    public string? SectionKey { get; init; }

    /// <summary>The Model-Role Config role requested, e.g. <c>"deep-reasoning"</c>.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("agent_role")]
    public required string AgentRole { get; init; }

    /// <summary>The Model-Role Config resolution that served this invocation (doc 08 §6).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("resolved_model")]
    public required ResolvedModelSummary ResolvedModel { get; init; }

    /// <summary>The wire type name of the invocation's input contract.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("input_contract_type")]
    public required string InputContractType { get; init; }

    /// <summary>SHA256 hex hash of the input's canonical bytes (doc 05 §6: hashes, not payloads).</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("input_hash")]
    public required string InputHash { get; init; }

    /// <summary>The wire type name of the invocation's output contract.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("output_contract_type")]
    public required string OutputContractType { get; init; }

    /// <summary>SHA256 hex hash of the output's canonical bytes (doc 05 §6: hashes, not payloads).</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("output_hash")]
    public required string OutputHash { get; init; }

    /// <summary>Prompt (input) tokens reported by the provider.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("input_tokens")]
    public required long InputTokens { get; init; }

    /// <summary>Completion (output) tokens reported by the provider.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("output_tokens")]
    public required long OutputTokens { get; init; }

    /// <summary>
    /// Aggregate cache-read tokens reported by the provider for this invocation (C-15:
    /// aggregate, not per-breakpoint — <see cref="CacheMetrics"/> carries the per-tier split).
    /// </summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("cache_read_tokens")]
    public required long CacheReadTokens { get; init; }

    /// <summary>Aggregate cache-write (cache-creation) tokens reported by the provider for this invocation (C-15).</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("cache_write_tokens")]
    public required long CacheWriteTokens { get; init; }

    /// <summary>How many retry attempts the activity needed before this invocation succeeded.</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("retry_count")]
    public required int RetryCount { get; init; }

    /// <summary>Wall-clock duration of the model call.</summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("latency_ms")]
    public required long LatencyMs { get; init; }

    /// <summary>MCP tool calls made during this invocation (ADR-CD6, S9.25); <c>[]</c> when the node declared no tools or none were called.</summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName("tool_calls")]
    public required IReadOnlyList<ToolCall> ToolCalls { get; init; }

    /// <summary>UTC timestamp at which the invocation was made.</summary>
    [JsonPropertyOrder(16)]
    [JsonPropertyName("invoked_at_utc")]
    public required DateTime InvokedAtUtc { get; init; }
}
