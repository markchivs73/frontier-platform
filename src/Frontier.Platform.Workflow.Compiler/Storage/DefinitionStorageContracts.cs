using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Storage;

/// <summary>Mutable draft document (one per workflow, ETag-guarded).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record DefinitionDraftDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; init; }
    [JsonPropertyName("state")]
    public required string State { get; init; } = "draft";
    [JsonPropertyName("baseVersion")]
    public required int BaseVersion { get; init; }
    [JsonPropertyName("draftRevision")]
    public required string DraftRevision { get; init; }
    [JsonPropertyName("definition")]
    [JsonConverter(typeof(MigratingWorkflowDefinitionConverter))]
    public required WorkflowDefinition Definition { get; init; }
    [JsonPropertyName("lastEditedBy")]
    public required string LastEditedBy { get; init; }
    [JsonPropertyName("lastEditedUtc")]
    public required DateTime LastEditedUtc { get; init; }
}

/// <summary>Immutable published version document (state: published → superseded → retired).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record DefinitionVersionDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; init; }
    [JsonPropertyName("state")]
    public required string State { get; init; }
    [JsonPropertyName("definitionVersion")]
    public required int DefinitionVersion { get; init; }
    [JsonPropertyName("definitionHash")]
    public required string DefinitionHash { get; init; }
    [JsonPropertyName("definition")]
    [JsonConverter(typeof(MigratingWorkflowDefinitionConverter))]
    public required WorkflowDefinition Definition { get; init; }
    [JsonPropertyName("proposedBy")]
    public required string ProposedBy { get; init; }
    [JsonPropertyName("approvedBy")]
    public required string ApprovedBy { get; init; }
    [JsonPropertyName("proposedUtc")]
    public required DateTime ProposedUtc { get; init; }
    [JsonPropertyName("approvedUtc")]
    public required DateTime ApprovedUtc { get; init; }
    [JsonPropertyName("validationReportRef")]
    public required string ValidationReportRef { get; init; }
    [JsonPropertyName("supersededByVersion")]
    public int? SupersededByVersion { get; init; }
    [JsonPropertyName("retirement")]
    public RetirementInfoDocument? Retirement { get; init; }
}

/// <summary>Current version pointer (always one per workflow).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record CurrentVersionPointerDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; init; }
    [JsonPropertyName("currentVersion")]
    public required int CurrentVersion { get; init; }
}

/// <summary>Validation report document (persisted for 90 days, or indefinitely if referenced by a published version).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record ValidationReportDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; init; }
    [JsonPropertyName("draftRevision")]
    public required string DraftRevision { get; init; }
    [JsonPropertyName("validatedAtUtc")]
    public required DateTime ValidatedAtUtc { get; init; }
    [JsonPropertyName("outcome")]
    public required ValidationOutcome Outcome { get; init; }
    [JsonPropertyName("findings")]
    public required IReadOnlyList<ValidationFinding> Findings { get; init; }
    [JsonPropertyName("resourceVersions")]
    public required IReadOnlyDictionary<string, string> ResourceVersions { get; init; }
}

/// <summary>Retirement information (nested in DefinitionVersionDocument).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record RetirementInfoDocument
{
    [JsonPropertyName("retiredAtUtc")]
    public required DateTime RetiredAtUtc { get; init; }
    [JsonPropertyName("retiredBy")]
    public required string RetiredBy { get; init; }
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

/// <summary>
/// S9.55 (doc 13 ADR-DC5): a per-published-version health projection written by the daily
/// re-validation sweep. A read-optimised sidecar (<c>{workflowId}:v{n}:health</c>) alongside
/// the immutable version document in the same <c>workflow-definitions</c> partition — rebuildable
/// from a fresh sweep, never fed back into orchestration state (invariant #5), so the version
/// document itself stays immutable.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record WorkflowHealthDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; init; }
    [JsonPropertyName("definitionVersion")]
    public required int DefinitionVersion { get; init; }
    /// <summary><c>healthy</c> (Pass), <c>warning</c> (PassWithWarnings), or <c>failing</c> (Fail) — the last sweep's resourced-tier outcome.</summary>
    [JsonPropertyName("healthStatus")]
    public required string HealthStatus { get; init; }
    /// <summary>Distinct rule ids of the Error-severity findings that make the version fail (empty when healthy/warning).</summary>
    [JsonPropertyName("failingRuleIds")]
    public required IReadOnlyList<string> FailingRuleIds { get; init; }
    /// <summary>Total finding count (errors + warnings + info) from the last sweep.</summary>
    [JsonPropertyName("findingCount")]
    public required int FindingCount { get; init; }
    [JsonPropertyName("checkedAtUtc")]
    public required DateTime CheckedAtUtc { get; init; }
}

/// <summary>
/// S9.56 (doc 13 ADR-DC5, C-34): a per-workflow usage/health rollup projection written by the
/// daily sweep from execution snapshots. A read-optimised sidecar (<c>{workflowId}:usage</c>) in
/// the same <c>workflow-definitions</c> partition — rebuildable, never fed back into orchestration
/// state (invariant #5). One per workflow that has ever executed; overwritten each sweep.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record WorkflowUsageDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; init; }
    /// <summary>
    /// Most recent terminal-execution checkpoint time (any age), or null if none has ever
    /// completed. Deliberately NOT <c>required</c>: the canonical profile omits nulls when
    /// writing, so a never-run workflow persists this key absent — marking it required would
    /// make the document unreadable the moment it is written (the A1 catalogue 500 fixed in
    /// S13.7f). Absent on the wire means "never run", which is exactly the default.
    /// </summary>
    [JsonPropertyName("lastRunAtUtc")]
    public DateTime? LastRunAtUtc { get; init; }
    /// <summary>Terminal executions in the trailing 30-day window.</summary>
    [JsonPropertyName("runCount30d")]
    public required int RunCount30d { get; init; }
    /// <summary>Failed executions within the same window (the numerator of the failure rate).</summary>
    [JsonPropertyName("failureCount30d")]
    public required int FailureCount30d { get; init; }
    /// <summary>Currently running or gate-paused executions of this workflow.</summary>
    [JsonPropertyName("activeCount")]
    public required int ActiveCount { get; init; }
    /// <summary>When this rollup was computed — drives the A1 staleness badge.</summary>
    [JsonPropertyName("sweptAtUtc")]
    public required DateTime SweptAtUtc { get; init; }
}

/// <summary>Publish proposal document (transient, auto-withdrawn on draft edit or explicit rejection).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record PublishProposalDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; init; }
    [JsonPropertyName("draftRevision")]
    public required string DraftRevision { get; init; }
    [JsonPropertyName("proposerId")]
    public required string ProposerId { get; init; }
    [JsonPropertyName("proposedAtUtc")]
    public required DateTime ProposedAtUtc { get; init; }
    [JsonPropertyName("validationReportRef")]
    public required ValidationReportRef ValidationReportRef { get; init; }
    [JsonPropertyName("state")]
    public required ProposalState State { get; init; }
    [JsonPropertyName("approverNoteOrReason")]
    public string? ApproverNoteOrReason { get; init; }
}

/// <summary>Reference to a validation report (immutable once proposed).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record ValidationReportRef
{
    [JsonPropertyName("documentId")]
    public required string DocumentId { get; init; }
}

/// <summary>Sandbox test-run document (ephemeral, 7-day TTL, advisory evidence only).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record TestRunDocument
{
    /// <summary>S9.38e (doc 13 §5): the fixed retention window for every <see cref="TestRunDocument"/> — 7 days, no Blob archival.</summary>
    public const int SandboxRetentionSeconds = 7 * 24 * 60 * 60;

    [JsonPropertyName("id")]
    public required string Id { get; init; } // SANDBOX-{runId}::{workflowId}
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; init; }
    [JsonPropertyName("testRunId")]
    public required string TestRunId { get; init; }
    [JsonPropertyName("draftRevision")]
    public required string DraftRevision { get; init; }
    [JsonPropertyName("startedAtUtc")]
    public required DateTime StartedAtUtc { get; init; }
    [JsonPropertyName("completedAtUtc")]
    public DateTime? CompletedAtUtc { get; init; }
    [JsonPropertyName("gateMode")]
    public required string GateMode { get; init; } // "AutoApprove" | "Interactive" (TestRunGateMode.ToString())
    // S9.85: running | paused_at_gate | completed | failed (TestRunStatus). Additive — legacy
    // documents (pre-S9.85) have no status and are read as terminal via CompletedAtUtc/Success
    // (their 7-day TTL makes migration moot).
    [JsonPropertyName("status")]
    public string? Status { get; init; }
    [JsonPropertyName("success")]
    public required bool Success { get; init; }
    // S9.53: per-node step metadata (node type, output contract/hash, section key, retries,
    // timing). Content is NOT stored here — it's read live from the section store at fetch time.
    [JsonPropertyName("nodeSteps")]
    public required IReadOnlyList<TestRunNodeStep> NodeSteps { get; init; }
    [JsonPropertyName("failureNodeId")]
    public string? FailureNodeId { get; init; }
    [JsonPropertyName("validatorFindings")]
    public required IReadOnlyList<ValidationFinding> ValidatorFindings { get; init; }
    [JsonPropertyName("costMetrics")]
    public required IReadOnlyDictionary<string, string> CostMetrics { get; init; }
    [JsonPropertyName("gateDecisions")]
    public required IReadOnlyList<TestRunGateDecision> GateDecisions { get; init; }
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }
    [JsonPropertyName("pausedAtGateId")]
    public string? PausedAtGateId { get; init; }
    [JsonPropertyName("gateKind")]
    public string? GateKind { get; init; }
    // S9.38e (doc 13 §5 "Cleanup"): Cosmos per-item TTL override, in seconds — the
    // workflow-definitions container's defaultTtl=-1 enables this per item without expiring
    // drafts/versions/proposals, which set no ttl. Always TestRunDocument.SandboxRetentionSeconds;
    // every TestRunDocument is a sandbox test-run by construction (there is no non-sandbox one).
    [JsonPropertyName("ttl")]
    public int? Ttl { get; init; }
}

/// <summary>Chat designer turn (one designer input + agent proposal + merge outcome).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record DesignTurnDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; } // {workflowId}:turn:{turnId}
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; init; }
    [JsonPropertyName("draftId")]
    public required string DraftId { get; init; } // {workflowId}:draft
    [JsonPropertyName("turnNumber")]
    public required int TurnNumber { get; init; }
    [JsonPropertyName("designerId")]
    public required string DesignerId { get; init; }
    [JsonPropertyName("createdAtUtc")]
    public required DateTime CreatedAtUtc { get; init; }
    [JsonPropertyName("designerMessage")]
    public required string DesignerMessage { get; init; }
    [JsonPropertyName("draftRevisionAtTurn")]
    public required string DraftRevisionAtTurn { get; init; }
    [JsonPropertyName("agentProposalJson")]
    public string? AgentProposalJson { get; init; } // WorkflowDefinition JSON
    [JsonPropertyName("proposalReasoningJson")]
    public string? ProposalReasoningJson { get; init; } // Agent's explanation
    [JsonPropertyName("proposalChanges")]
    public IReadOnlyList<ProposalChangeItem>? ProposalChanges { get; init; } // S9.10: server-computed diff (doc 14 §4.1)
    [JsonPropertyName("proposalBlockReason")]
    public string? ProposalBlockReason { get; init; } // S9.10: pure-tier validation failure on the proposal; null = mergeable
    [JsonPropertyName("mergeOutcome")]
    public string? MergeOutcome { get; init; } // "merged" | "conflict" | "rejected"
    [JsonPropertyName("conflictSummary")]
    public string? ConflictSummary { get; init; } // If outcome is "conflict"
    [JsonPropertyName("mentions")]
    public IReadOnlyList<ValidatedMention>? Mentions { get; init; } // S9.33: doc 14 §8a resource mentions, server-validated
}

/// <summary>A page of workflow catalogue entries for the A1 listing (doc 20).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record WorkflowCataloguePage(
    IReadOnlyList<WorkflowCatalogueSummary> Items,
    int Total);

/// <summary>Minimal workflow projection for catalogue listing (A1).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record WorkflowCatalogueSummary(
    string WorkflowId,
    string Name,
    string? EngagementType,
    string Status,
    DateTime? LastPublishedAt,
    string? LastPublishedBy);

/// <summary>Chat history index (one per draft, tracks turns).</summary>
[ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
public sealed record ChatHistoryDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; } // {workflowId}:chat-history
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; init; }
    [JsonPropertyName("draftId")]
    public required string DraftId { get; init; }
    [JsonPropertyName("nextTurnNumber")]
    public required int NextTurnNumber { get; init; }
    [JsonPropertyName("lastMessageAtUtc")]
    public required DateTime LastMessageAtUtc { get; init; }
    [JsonPropertyName("turnDocumentIds")]
    public required IReadOnlyList<string> TurnDocumentIds { get; init; }
}
