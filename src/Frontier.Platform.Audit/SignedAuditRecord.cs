using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// The signed, chained audit record for one execution (doc 05 §3, all 18 fields) —
/// <c>IAuditSigner.SignAsync</c>'s output, persisted to <c>audit-records</c> (doc 05 §6).
/// Fields 0-13 mirror <see cref="AuditRecord"/>; fields 14-17 add the per-engagement hash
/// chain and HMAC signature.
/// </summary>
public sealed record SignedAuditRecord : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The DTF instance id this record was consolidated from: <c>{engagementId}::{workflowId}</c>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>The engagement this execution belongs to — the audit chain's governance unit (doc 05 §5).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The workflow's stable identity.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("workflow_id")]
    public required string WorkflowId { get; init; }

    /// <summary>The <c>WorkflowDefinition.DefinitionVersion</c> this execution was pinned to (ADR-2).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("definition_version")]
    public required int DefinitionVersion { get; init; }

    /// <summary>The exact definition graph that ran — graph-version accountability (doc 05 §7 query 8).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("definition_hash")]
    public required string DefinitionHash { get; init; }

    /// <summary>UTC timestamp at which the execution started.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("started_at_utc")]
    public required DateTime StartedAtUtc { get; init; }

    /// <summary>UTC timestamp at which the execution closed and this record was consolidated.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("closed_at_utc")]
    public required DateTime ClosedAtUtc { get; init; }

    /// <summary>The execution's terminal status.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("final_status")]
    public required ExecutionStatus FinalStatus { get; init; }

    /// <summary>DTF history events, ordered by DTF sequence (doc 05 §4 step 4) — the timeline's spine.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("orchestration_events")]
    public required IReadOnlyList<WorkflowEvent> OrchestrationEvents { get; init; }

    /// <summary>Agent invocations made during this execution, consolidated from staged telemetry (doc 05 §4 step 2).</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("agent_invocations")]
    public required IReadOnlyList<AgentInvocation> AgentInvocations { get; init; }

    /// <summary>Check-agent validation outcomes; always <c>[]</c> until Stage 6.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("validator_outcomes")]
    public required IReadOnlyList<ValidatorOutcome> ValidatorOutcomes { get; init; }

    /// <summary>Human gate decisions made during this execution, lifted from <c>ExecutionSnapshot.Decisions</c>.</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("human_decisions")]
    public required IReadOnlyList<HumanDecisionRecord> HumanDecisions { get; init; }

    /// <summary>Per-tier cache activity aggregated across <see cref="AgentInvocations"/> (C-15).</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("cache_metrics")]
    public required CacheMetrics CacheMetrics { get; init; }

    /// <summary>
    /// The engagement's prior execution record's <see cref="RecordHash"/>, chaining this
    /// record to its predecessor (doc 05 §5); genesis (no prior record) is
    /// <c>SHA-256(engagementId)</c>.
    /// </summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("previous_record_hash")]
    public required string PreviousRecordHash { get; init; }

    /// <summary>SHA-256 of this record's canonical bytes for fields 0-14 (doc 05 §5).</summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName("record_hash")]
    public required string RecordHash { get; init; }

    /// <summary>HMAC-SHA256(<see cref="RecordHash"/>, signing key) (doc 05 §5).</summary>
    [JsonPropertyOrder(16)]
    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    /// <summary>The signing key's version identifier (doc 05 §5) — verification resolves the key by this id.</summary>
    [JsonPropertyOrder(17)]
    [JsonPropertyName("signing_key_id")]
    public required string SigningKeyId { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (DefinitionVersion < 1)
        {
            violations.Add("definition_version must be at least 1.");
        }

        if (ClosedAtUtc < StartedAtUtc)
        {
            violations.Add("closed_at_utc must not be before started_at_utc.");
        }

        if (AgentInvocations.Any(invocation => string.IsNullOrWhiteSpace(invocation.CorrelationId)))
        {
            violations.Add("every agent invocation must have a correlation_id.");
        }

        if (string.IsNullOrWhiteSpace(PreviousRecordHash) || string.IsNullOrWhiteSpace(RecordHash) ||
            string.IsNullOrWhiteSpace(Signature) || string.IsNullOrWhiteSpace(SigningKeyId))
        {
            violations.Add("previous_record_hash, record_hash, signature, and signing_key_id must all be present.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(SignedAuditRecord), violations);
        }
    }
}
