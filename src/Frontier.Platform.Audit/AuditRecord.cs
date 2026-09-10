using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// The unsigned audit record for one execution (doc 05 §3 <c>SignedAuditRecord</c> fields
/// 0-13) — the audit consolidator's output before <c>IAuditSigner</c> chains and signs it
/// into a <see cref="SignedAuditRecord"/>.
/// </summary>
public sealed record AuditRecord : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "2.0";

    /// <summary>The DTF instance id this record was consolidated from — an opaque run token (ADR-PA20); <c>EngagementId</c>/<c>WorkflowId</c>/<c>RunId</c> are the fields beside it.</summary>
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
    /// S9.38e (doc 13 §5 "Evidence pollution"): <c>true</c> for a sandbox test-run execution
    /// (<c>SANDBOX-</c>-prefixed <see cref="EngagementId"/>) — <c>null</c> (omitted on the
    /// wire, canonical-serialization skill) for every real execution, so existing golden-file
    /// bytes are unaffected. Excluded from maturity tracking / empirical aggregates / the
    /// canvas overlay wherever those eventually query <see cref="AuditRecord"/> — the Phase 1
    /// implementations of both are empty stubs (S7+ scope, doc 11 §5/§6), so there is no real
    /// query surface to filter yet; this field exists so the filter is a one-line addition when
    /// that aggregation layer lands, not a retrofit.
    /// </summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("sandbox")]
    public bool? Sandbox { get; init; }

    /// <summary>
    /// The dynamic-context epoch the execution was assembled from (S13.60): the input-side half of
    /// Article 12's "traceability of results to inputs", beside <see cref="DefinitionHash"/>'s
    /// system-version half. <see langword="null"/> (omitted on the wire) for records written before
    /// the pin existed, so existing golden and signed bytes are unaffected.
    /// </summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName("dynamic_context_epoch")]
    public int? DynamicContextEpoch { get; init; }

    /// <summary>The store's content hash of that epoch — self-verifying against the <c>engagement-context</c> document it names.</summary>
    [JsonPropertyOrder(16)]
    [JsonPropertyName("dynamic_context_hash")]
    public string? DynamicContextHash { get; init; }

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

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(AuditRecord), violations);
        }
    }
}
