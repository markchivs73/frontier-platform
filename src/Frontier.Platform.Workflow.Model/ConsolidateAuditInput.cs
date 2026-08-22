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
    /// <summary>The DTF instance id: <c>{engagementId}::{workflowId}</c> (rule 3).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>The pinned definition's <c>DefinitionHash</c> (ADR-2: rides inline as orchestration input).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("definition_hash")]
    public required string DefinitionHash { get; init; }

    /// <summary>UTC timestamp at which the orchestration started, captured once via <c>context.CurrentUtcDateTime</c>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("started_at_utc")]
    public required DateTime StartedAtUtc { get; init; }
}
