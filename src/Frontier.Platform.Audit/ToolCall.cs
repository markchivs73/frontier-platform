using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// A single MCP tool invocation made during an <see cref="AgentInvocation"/> (doc 05 §3),
/// via MAF-native tool-calling on <c>AgentTaskNode</c> (ADR-CD6, S9.25).
/// <see cref="AgentInvocation.ToolCalls"/> and <see cref="AuditTelemetryRecord.ToolCalls"/>
/// are <c>[]</c> when the node declares no <c>AgentTaskNode.ToolRefs</c> or the
/// model never calls one.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record ToolCall
{
    /// <summary>The MCP tool reference invoked, e.g. <c>"connectors/crm.create_opportunity"</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>UTC timestamp at which the tool was invoked.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("invoked_at_utc")]
    public required DateTime InvokedAtUtc { get; init; }

    /// <summary>
    /// S9.38c (doc 13 §5): <c>true</c> when this was a sandbox test-run write short-circuited
    /// to a synthetic ack rather than actually reaching the connector — <c>null</c> (omitted
    /// on the wire, canonical-serialization skill) for every real invocation, so existing
    /// golden-file bytes are unaffected.
    /// </summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("simulated")]
    public bool? Simulated { get; init; }

    /// <summary>
    /// ADR-PA28: how the platform came to know about this call — <see cref="ToolCallProvenance.Observed"/>
    /// when the platform made it, <see cref="ToolCallProvenance.SelfReported"/> when a remote agent
    /// claimed it in its reply. Platform writers never set it, so it is absent (omitted on the wire)
    /// on every record written before this field existed, and an absent provenance reads as observed.
    /// </summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("provenance")]
    public ToolCallProvenance? Provenance { get; init; }

    /// <summary>
    /// ADR-PA28: the agent's stated purpose for a self-reported call; <c>null</c> (omitted on the wire)
    /// for an observed one. Capped at <see cref="ToolCallNoteCap.MaxLength"/> (200) characters
    /// (ADR-E1 tonnage) — enforced by the containing record's <c>Validate()</c>.
    /// </summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("note")]
    public string? Note { get; init; }
}
