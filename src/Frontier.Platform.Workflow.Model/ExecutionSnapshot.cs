using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// The queryable projection of execution state at a checkpoint (doc 00 §3.4, doc 02 §2).
/// DTF's event-sourced history remains the authoritative record; this is what Artifact
/// State persists to Cosmos after each checkpoint so the UI and crash-recovery worker
/// can answer "what's running, what's approved, where is it paused" without replay.
/// </summary>
public sealed record ExecutionSnapshot : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "2.0"; // S13.12a: the ADR-E3a D3 artifact rename is a wire break (ArtifactVocabularyMigration adapts 1.0 bytes).

    /// <summary>The DTF instance id: <c>{engagementId}::{workflowId}</c> (dispatcher children append <c>::{workItemId}</c>).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>The engagement this execution belongs to.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The workflow's stable identity.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("workflow_id")]
    public required string WorkflowId { get; init; }

    /// <summary>The <see cref="WorkflowDefinition.DefinitionVersion"/> this execution is pinned to (ADR-2).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("definition_version")]
    public required int DefinitionVersion { get; init; }

    /// <summary>Monotonic checkpoint counter for this execution; one per checkpoint.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("sequence")]
    public required int Sequence { get; init; }

    /// <summary>The execution's current lifecycle status.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("status")]
    public required ExecutionStatus Status { get; init; }

    /// <summary>The node currently executing, if any.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("current_node_id")]
    public string? CurrentNodeId { get; init; }

    /// <summary>The gate node id this execution is paused at, when <see cref="Status"/> is <see cref="ExecutionStatus.PausedAtGate"/>.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("paused_at_gate_id")]
    public string? PausedAtGateId { get; init; }

    /// <summary>Status of every artifact this workflow produces, keyed by artifact key (ADR-E3a D3).</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("artifacts")]
    public required IReadOnlyDictionary<string, ArtifactStatus> Artifacts { get; init; }

    /// <summary>Completed steps in execution order.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("completed_steps")]
    public required IReadOnlyList<StepCompletion> CompletedSteps { get; init; }

    /// <summary>Human gate decisions recorded so far.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("decisions")]
    public required IReadOnlyList<HitlDecision> Decisions { get; init; }

    /// <summary>Artifact key → blob/document reference of its last-approved snapshot, for rollback.</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("approved_snapshot_refs")]
    public required IReadOnlyDictionary<string, string> ApprovedSnapshotRefs { get; init; }

    /// <summary>UTC timestamp at which this checkpoint was written.</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("checkpointed_at_utc")]
    public required DateTime CheckpointedAtUtc { get; init; }

    /// <summary>
    /// The reason code (doc 10 §3's taxonomy, e.g. <c>"contract_violation"</c>,
    /// <c>"guardrail"</c>, <c>"unclassified"</c>) for the permanent step failure this
    /// execution paused on, when <see cref="Status"/> is <see cref="ExecutionStatus.PausedOnFailure"/>
    /// (S9.45, doc 03 §9/§10, doc 19 §B3's alert band). Additive (canonical-serialization
    /// minor-change rule): absent on every snapshot written before this field existed.
    /// </summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("failure_classification")]
    public string? FailureClassification { get; init; }

    /// <summary>
    /// The directing human this execution runs for (ADR-E8, S13.19), threaded from the
    /// start threshold (API caller's claims, or the work item's <c>directed_by</c> for
    /// dispatcher children) — the root of the derived-attribution chain for every
    /// agent/tool action in the execution. Additive (canonical-serialization minor-change
    /// rule): absent on every snapshot written before this field existed.
    /// </summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName("initiated_by")]
    public string? InitiatedBy { get; init; }

    /// <summary>
    /// Nodes this execution skipped because every path to them was dead — i.e. they sat
    /// only on unselected <see cref="DecisionNode"/> branches (ADR-5 Decision 6, S13.7j).
    /// Additive (canonical-serialization minor-change rule): absent on every snapshot
    /// written before this field existed, and absent when nothing was skipped.
    /// </summary>
    [JsonPropertyOrder(16)]
    [JsonPropertyName("skipped_node_ids")]
    public IReadOnlyList<string>? SkippedNodeIds { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (DefinitionVersion < 1)
        {
            violations.Add("definition_version must be at least 1.");
        }

        if (Sequence < 0)
        {
            violations.Add("sequence must not be negative.");
        }

        if (Status == ExecutionStatus.PausedAtGate && string.IsNullOrWhiteSpace(PausedAtGateId))
        {
            violations.Add("paused_at_gate_id is required when status is paused_at_gate.");
        }

        if (Status == ExecutionStatus.PausedOnFailure && string.IsNullOrWhiteSpace(FailureClassification))
        {
            violations.Add("failure_classification is required when status is paused_on_failure.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(ExecutionSnapshot), violations);
        }
    }
}
