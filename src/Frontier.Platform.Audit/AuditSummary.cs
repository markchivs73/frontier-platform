using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// One row of an <see cref="AuditQuery"/> result (doc 05 §10
/// <c>GET /api/audit/query</c>) — a <see cref="SignedAuditRecord"/>'s identifying fields,
/// without its event/invocation/decision lists. Callers fetch the full record via
/// <c>GET /api/audit/{executionId}</c> for detail.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record AuditSummary
{
    /// <summary>The DTF instance id this record was consolidated from.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>The engagement this execution belongs to.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The workflow's stable identity.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("workflow_id")]
    public required string WorkflowId { get; init; }

    /// <summary>The definition version this execution was pinned to.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("definition_version")]
    public required int DefinitionVersion { get; init; }

    /// <summary>The exact definition graph that ran (doc 05 §7 query 8).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("definition_hash")]
    public required string DefinitionHash { get; init; }

    /// <summary>The execution's terminal status.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("final_status")]
    public required ExecutionStatus FinalStatus { get; init; }

    /// <summary>UTC timestamp at which the execution started.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("started_at_utc")]
    public required DateTime StartedAtUtc { get; init; }

    /// <summary>UTC timestamp at which the execution closed and this record was signed.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("closed_at_utc")]
    public required DateTime ClosedAtUtc { get; init; }
}
