using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Input to <c>ConsolidateAuditActivity</c> (doc 05 §8, S5.4): the facts the orchestrator
/// already holds and that have no source in the final <see cref="ExecutionSnapshot"/> or
/// in <c>AuditTelemetryRecord</c>s. Carrying them here means the audit consolidator
/// never needs a DTF history read or a <c>workflow-definitions</c> point-read —
/// <see cref="DefinitionHash"/> rides inline on the orchestration input's definition per
/// ADR-2, and <see cref="StartedAtUtc"/> is <c>context.CurrentUtcDateTime</c> captured once
/// at orchestration start (deterministic). Not an <see cref="IVersionedContract"/> — a DTF
/// activity input, like the <c>AgentTaskActivity*</c> family in
/// <c>Frontier.Reason.Workflow.Orchestration</c>.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by round-trip/byte-stability tests.")]
public sealed record ConsolidateAuditInput
{
    /// <summary>The DTF instance id — an addressing key, never read for its parts (ADR-PA15).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>
    /// The engagement this execution belongs to, carried explicitly (ADR-PA15). The consolidator
    /// needs it before it can read the snapshot — it is the snapshot container's partition key —
    /// so it cannot come from the snapshot it is used to fetch, which is why the id used to be
    /// parsed for it.
    /// </summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The workflow's stable identity, carried explicitly (ADR-PA15).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("workflow_id")]
    public required string WorkflowId { get; init; }

    /// <summary>The pinned definition's <c>DefinitionHash</c> (ADR-2: rides inline as orchestration input).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("definition_hash")]
    public required string DefinitionHash { get; init; }

    /// <summary>UTC timestamp at which the orchestration started, captured once via <c>context.CurrentUtcDateTime</c>.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("started_at_utc")]
    public required DateTime StartedAtUtc { get; init; }
}
