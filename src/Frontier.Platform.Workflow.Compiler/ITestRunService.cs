using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Abstractions;


namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Sandbox test-run service: execute a draft definition in a fenced environment before publishing.
/// Doc 13 §5: cost ceiling, dry-run external writes, HITL gate modes, audit-stamped results.
/// </summary>
public interface ITestRunService
{
    /// <summary>
    /// Start a sandbox test-run of a draft definition. S9.85: returns as soon as the real
    /// orchestration is started and the run document is persisted with
    /// <see cref="TestRunStatus.Running"/> — it never waits for the run (the S9.38a bounded
    /// poll is gone). Progress is observed via <see cref="ReconcileAsync"/>-backed reads.
    /// </summary>
    Task<TestRunHandle> StartAsync(
        string workflowId,
        TestRunRequest request,
        CancellationToken ct);

    /// <summary>Get the result of a test-run — live state for a run still executing (via <see cref="ReconcileAsync"/>), the persisted outcome once terminal.</summary>
    Task<TestRunResult?> GetResultAsync(
        string testRunId,
        CancellationToken ct);

    /// <summary>
    /// S9.85 (doc 13 §5, `docs/archive/S9-ASYNC-TESTRUN-PLAN.md` §4.1): the shared reconciler — for a
    /// non-terminal run, observe the live execution snapshot, advance an auto-approve run paused
    /// at a gate (same HITL path a designer decision uses), project the live per-step state, and
    /// persist the run document only on a transition (gate pause / terminal) — idempotent, so
    /// concurrent callers (an A4 viewer poll racing the S9.86 finalizer sweep) are safe. Called
    /// from every read path and the worker finalizer. Returns the freshest result, or <c>null</c>
    /// if <paramref name="testRunId"/> is unknown.
    /// </summary>
    Task<TestRunResult?> ReconcileAsync(
        string testRunId,
        CancellationToken ct);

    /// <summary>
    /// S9.38d/S9.85: decides a pending gate on an interactive test-run — raises the decision on
    /// the real execution, then reconciles once and returns the run's current state (it does
    /// <b>not</b> wait for the orchestration to process the decision; the caller's next poll
    /// observes what happens next). Returns <c>null</c> if <paramref name="testRunId"/> is unknown.
    /// </summary>
    Task<TestRunResult?> DecideGateAsync(
        string testRunId,
        string gateId,
        DecisionKind decision,
        string? note,
        CancellationToken ct);
}

/// <summary>
/// S9.85: the wire/storage status of a sandbox test-run
/// (`running → paused_at_gate → … → completed | failed`). Stored on
/// <c>TestRunDocument.Status</c>; legacy documents (pre-S9.85, no status) derive terminal
/// state from <c>CompletedAtUtc</c>/<c>Success</c>.
/// </summary>
public static class TestRunStatus
{
    /// <summary>The orchestration is executing (or not yet checkpointed).</summary>
    public const string Running = "running";

    /// <summary>Paused at an interactive gate awaiting a designer decision.</summary>
    public const string PausedAtGate = "paused_at_gate";

    /// <summary>Terminal: the run completed successfully.</summary>
    public const string Completed = "completed";

    /// <summary>Terminal: the run failed (including permanent pause-on-failure and cancellation).</summary>
    public const string Failed = "failed";
}

/// <summary>Request to start a test-run (doc 13 §5 surface).</summary>
public sealed record TestRunRequest
{
    /// <summary>Sample input contract data for the workflow entry node.</summary>
    public required object SampleInputs { get; init; }

    /// <summary>HITL gate behaviour: auto-approve (flow-through) or interactive (designer gates).</summary>
    public required TestRunGateMode GateMode { get; init; }
}

/// <summary>HITL gate behaviour during test-run (doc 13 §5 fencing).</summary>
public enum TestRunGateMode
{
    /// <summary>Gates auto-approve; test-run flows through to completion.</summary>
    AutoApprove,

    /// <summary>Designer plays approver; can exercise reject/rollback/cascade paths.</summary>
    Interactive
}

/// <summary>Handle returned from StartAsync, identifies a test-run in progress.</summary>
public sealed record TestRunHandle
{
    /// <summary>Unique test-run ID in format SANDBOX-{runId}::{workflowId}.</summary>
    public required string TestRunId { get; init; }

    /// <summary>Workflow being tested.</summary>
    public required string WorkflowId { get; init; }

    /// <summary>Time the test-run started (UTC).</summary>
    public required DateTime StartedAtUtc { get; init; }
}

/// <summary>Result of a completed test-run (doc 13 §5 advisory evidence).</summary>
public sealed record TestRunResult
{
    /// <summary>Unique test-run ID (SANDBOX-{runId}::{workflowId}).</summary>
    public required string TestRunId { get; init; }

    /// <summary>Draft revision that was tested.</summary>
    public required string DraftRevision { get; init; }

    /// <summary>Whether the run completed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>S9.85: the run's current <see cref="TestRunStatus"/> (additive — <c>null</c> only from pre-S9.85 fakes/docs, read as terminal via <see cref="Success"/>).</summary>
    public string? Status { get; init; }

    /// <summary>S9.53 (doc 19 A4-R4): per-step rows for the nodes that completed, each carrying the node's real output + metadata for the expandable A4 result panel.</summary>
    public required IReadOnlyList<TestRunNodeStep> NodeSteps { get; init; }

    /// <summary>
    /// C-35 (S9.53): for a plain failure (no gate involved), the last *completed* node id — the
    /// anchor for the A4 "failure at or after this step" canvas link. <see langword="null"/> on
    /// success or a gate pause. Not the exact failing node (deferred, S9.45's honest gap).
    /// </summary>
    public string? FailureNodeId { get; init; }

    /// <summary>Validator results from rules executed during test-run.</summary>
    public required IReadOnlyList<ValidationFinding> ValidatorFindings { get; init; }

    /// <summary>Token usage and cost actuals from LLM invocations.</summary>
    public required TestRunCostMetrics CostMetrics { get; init; }

    /// <summary>HITL decisions made during test-run (if gateMode=Interactive).</summary>
    public required IReadOnlyList<TestRunGateDecision> GateDecisions { get; init; }

    /// <summary>Time the test-run completed (UTC).</summary>
    public required DateTime CompletedAtUtc { get; init; }

    /// <summary>Error message if Success=false (null if successful).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>S9.38d: the gate node id this run is currently paused at (interactive mode), or <c>null</c> if not paused.</summary>
    public string? PausedAtGateId { get; init; }

    /// <summary>S9.38d: <see cref="PausedAtGateId"/>'s <c>GateKind</c>, for the A4 UI's gate-pending card.</summary>
    public string? GateKind { get; init; }
}

/// <summary>
/// S9.53 (doc 19 A4-R4, doc 13 §6): one node's contribution to a test-run, shown as an
/// expandable row on the A4 result panel. Metadata comes from the execution snapshot's
/// completed step; <see cref="OutputContent"/> is the node's real section output, read back
/// from the section store at result-fetch time (null when the node wrote no section, or the
/// content expired under the sandbox TTL).
/// </summary>
public sealed record TestRunNodeStep
{
    /// <summary>The node id.</summary>
    public required string NodeId { get; init; }

    /// <summary>Execution status of this step (currently always <c>completed</c> — only completed steps are recorded).</summary>
    public required string Status { get; init; }

    /// <summary>The node type (smart-enum name), e.g. <c>agent_task</c>.</summary>
    public string? NodeType { get; init; }

    /// <summary>The contract type of the node's output, e.g. <c>ScopeSection</c>.</summary>
    public string? OutputContractType { get; init; }

    /// <summary>SHA-256 hash of the node's canonical output bytes (doc 05).</summary>
    public string? OutputHash { get; init; }

    /// <summary>The section this node wrote (maps the node to its <see cref="OutputContent"/>).</summary>
    public string? ArtifactKey { get; init; }

    /// <summary>Retries the node needed before it completed.</summary>
    public int RetryCount { get; init; }

    /// <summary>When this step completed (UTC).</summary>
    public DateTime? CompletedAtUtc { get; init; }

    /// <summary>Derived per-step duration (delta from the previous completed step), in ms; null for the first step.</summary>
    public int? DurationMs { get; init; }

    /// <summary>The node's real output content (JSON), read from the section store at fetch time; null when unavailable.</summary>
    public string? OutputContent { get; init; }

    /// <summary>
    /// S13.31 (doc 19 A4-R4): for a <c>decision</c> step, the branch it routed to
    /// (<see cref="Abstractions.StepCompletion.SelectedBranchNodeId"/>, S13.7j) — a decision
    /// produces no payload, so the routing outcome IS its result. Null for every other node type.
    /// </summary>
    public string? SelectedBranchNodeId { get; init; }
}

/// <summary>Cost metrics from a test-run (doc 13 §5 advisory evidence).</summary>
public sealed record TestRunCostMetrics
{
    /// <summary>Total tokens consumed across all LLM invocations in the test-run.</summary>
    public required int TotalTokens { get; init; }

    /// <summary>Input tokens.</summary>
    public required int InputTokens { get; init; }

    /// <summary>Output tokens.</summary>
    public required int OutputTokens { get; init; }

    /// <summary>Cache read tokens (if provider supports caching).</summary>
    public required int CacheReadTokens { get; init; }

    /// <summary>Cache write tokens (if provider supports caching).</summary>
    public required int CacheWriteTokens { get; init; }

    /// <summary>Estimated cost (vendor-specific calculation).</summary>
    public required decimal EstimatedCost { get; init; }

    /// <summary>Whether the test-run exceeded the Guardrails sandbox budget ceiling.</summary>
    public required bool BudgetExceeded { get; init; }
}

/// <summary>HITL gate decision made during an interactive test-run.</summary>
[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated members - S9.24 frozen policy")]
public sealed record TestRunGateDecision
{
    /// <summary>Gate node ID.</summary>
    public required string GateId { get; init; }

    /// <summary>Decision: approved or rejected.</summary>
    public required TestRunGateOutcome Outcome { get; init; }

    /// <summary>Optional note from the designer (reason for rejection, etc.).</summary>
    public string? Note { get; init; }

    /// <summary>Time the decision was made (UTC).</summary>
    public required DateTime DecidedAtUtc { get; init; }
}

/// <summary>HITL gate outcome in a test-run.</summary>
public enum TestRunGateOutcome
{
    /// <summary>Gate approved; execution continues.</summary>
    Approved,

    /// <summary>Gate rejected; execution rolls back to rollback target and cascades.</summary>
    Rejected
}
