using Frontier.Platform.Abstractions;

using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Sandbox test-run service: executes draft definitions in a fenced environment with cost ceiling and HITL gates.
/// Doc 13 §5: test-runs are advisory evidence, not blocking. Supports auto-approve and interactive gate modes.
/// S9.85 (`docs/archive/S9-ASYNC-TESTRUN-PLAN.md`): <see cref="StartAsync"/> is asynchronous — it starts the real
/// <c>GraphOrchestrator</c> run (S9.38a's <see cref="ITestRunExecutor"/> seam), persists the run document as
/// <see cref="TestRunStatus.Running"/>, and returns immediately; the S9.38a bounded poll (1s/120s, which failed
/// any realistic run) is gone. Progress is observed through <see cref="ReconcileAsync"/> — the shared reconciler
/// every read path and the S9.86 finalizer sweep call: observe the live snapshot, advance an auto-approve run
/// paused at a gate, project live per-step state, and persist only on a transition (idempotent, so a viewer
/// poll racing the sweep is safe). Reads-that-repair has precedent: S9.45's snapshot healing on read.
/// </summary>
public sealed class TestRunService : ITestRunService
{
    /// <summary>The system approver id stamped on auto-approved sandbox gate decisions (S9.38a).</summary>
    internal const string AutoApproveApproverId = "system:sandbox-auto-approve";

    /// <summary>The system approver id stamped on designer-decided sandbox gate decisions (S9.38d).</summary>
    internal const string DesignerApproverId = "system:sandbox-designer";

    private readonly IDefinitionStore _store;
    private readonly IDefinitionCompiler _compiler;
    private readonly ITestRunExecutor _executor;
    private readonly ITestRunTelemetryReader _telemetry;
    private readonly ITestRunArtifactReader _sections;

    public TestRunService(
        IDefinitionStore store,
        IDefinitionCompiler compiler,
        ITestRunExecutor executor,
        ITestRunTelemetryReader telemetry,
        ITestRunArtifactReader sections)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(sections);
        _store = store;
        _compiler = compiler;
        _executor = executor;
        _telemetry = telemetry;
        _sections = sections;
    }

    /// <inheritdoc />
    public async Task<TestRunHandle> StartAsync(string workflowId, TestRunRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(workflowId);
        ArgumentNullException.ThrowIfNull(request);

        var draft = await _store.GetDraftAsync(workflowId, ct);
        if (draft is null)
            throw new InvalidOperationException($"No draft found for workflow {workflowId}");

        var now = DateTime.UtcNow;
        var findings = _compiler.ValidateStructural(draft.Definition);
        if (findings.Any(f => f.Severity == ValidationSeverity.Error))
            return await PersistBlockedRunAsync(workflowId, draft.DraftRevision, request.GateMode, now, findings, ct);

        var engagementId = $"SANDBOX-{Guid.NewGuid():N}";
        var testRunId = await _executor.StartAsync(engagementId, draft.Definition, ct);

        var runningOutcome = new TestRunOutcome(
            Success: false, NodeSteps: [], ErrorMessage: null,
            PausedAtGateId: null, GateKind: null, GateDecisions: [], FailureNodeId: null);
        await CreateRunDocumentAsync(
            workflowId, testRunId, draft.DraftRevision, request.GateMode, now,
            TestRunStatus.Running, runningOutcome, completedAtUtc: null, findings, ct);

        return new TestRunHandle { TestRunId = testRunId, WorkflowId = workflowId, StartedAtUtc = now };
    }

    /// <inheritdoc />
    public async Task<TestRunResult?> GetResultAsync(string testRunId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(testRunId))
            return null;

        return await ReconcileAsync(testRunId, ct);
    }

    /// <inheritdoc />
    public async Task<TestRunResult?> ReconcileAsync(string testRunId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(testRunId);

        var doc = await _store.GetTestRunAsync(testRunId, ct);
        if (doc is null)
            return null;

        if (IsTerminalDocument(doc))
            return await ToResultAsync(doc, ct);

        var snapshot = await _executor.GetSnapshotAsync(doc.TestRunId, ExtractEngagementId(doc.TestRunId), ct);
        if (snapshot is null)
            return await ToResultAsync(doc, ct); // started but not yet checkpointed — still running

        if (snapshot.Status == ExecutionStatus.PausedAtGate && IsAutoApprove(doc) && snapshot.PausedAtGateId is { } gateId)
        {
            // Advance (policy): the deleted S9.38a poll loop auto-approved in-loop at 1s; the
            // reconciler advances one gate per invocation instead — the A4 viewer poll (~2s) and
            // the S9.86 sweep both drive it. No persist: the gate pause is being consumed.
            await _executor.RaiseGateDecisionAsync(doc.TestRunId, gateId, AutoApproveApproverId, DecisionKind.Approve, null, ct);
            return await BuildLiveResultAsync(doc, snapshot, ct);
        }

        if (TestRunExecution.IsTerminal(snapshot.Status))
        {
            var outcome = TestRunExecution.ToOutcome(snapshot);
            var persisted = await UpdateRunDocumentAsync(
                doc, outcome,
                snapshot.Status == ExecutionStatus.Completed ? TestRunStatus.Completed : TestRunStatus.Failed,
                completedAtUtc: DateTime.UtcNow, ct);
            return await ToResultAsync(persisted, ct);
        }

        if (snapshot.Status == ExecutionStatus.PausedAtGate)
        {
            var outcome = await WithGateKindAsync(TestRunExecution.ToOutcome(snapshot), doc.WorkflowId, ct);
            if (IsGateTransition(doc, outcome))
                doc = await UpdateRunDocumentAsync(doc, outcome, TestRunStatus.PausedAtGate, completedAtUtc: null, ct);
            return await ToResultAsync(doc, ct);
        }

        return await BuildLiveResultAsync(doc, snapshot, ct);
    }

    /// <summary>
    /// S9.38d/S9.85: raises the designer's gate decision on the real execution, then reconciles
    /// once and returns the run's current state — it does not wait for the orchestration to
    /// process the decision (the caller's next poll observes the continuation, or the rejection's
    /// rollback/regeneration cascade).
    /// </summary>
    public async Task<TestRunResult?> DecideGateAsync(string testRunId, string gateId, DecisionKind decision, string? note, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(testRunId);
        ArgumentException.ThrowIfNullOrEmpty(gateId);
        ArgumentNullException.ThrowIfNull(decision);

        var doc = await _store.GetTestRunAsync(testRunId, ct);
        if (doc is null)
            return null;

        await _executor.RaiseGateDecisionAsync(testRunId, gateId, DesignerApproverId, decision, note, ct);

        return await ReconcileAsync(testRunId, ct);
    }

    /// <summary>Terminal per the S9.85 <see cref="TestRunStatus"/>; a legacy document (no status, pre-S9.85, ≤7-day TTL) is terminal when its single end-of-run persist happened (<c>CompletedAtUtc</c> set). Public: the API head's runs-list uses it to decide which rows to reconcile.</summary>
    public static bool IsTerminalDocument(TestRunDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return doc.Status switch
        {
            TestRunStatus.Completed or TestRunStatus.Failed => true,
            TestRunStatus.Running or TestRunStatus.PausedAtGate => false,
            _ => doc.CompletedAtUtc is not null,
        };
    }

    /// <summary>Whether the run was started in auto-approve gate mode (persisted as <see cref="TestRunGateMode"/>'s name).</summary>
    internal static bool IsAutoApprove(TestRunDocument doc) =>
        Enum.TryParse<TestRunGateMode>(doc.GateMode, ignoreCase: true, out var mode) && mode == TestRunGateMode.AutoApprove;

    /// <summary>
    /// Persist-on-transition guard for a gate pause: first pause, a different gate (rollback
    /// re-walk reaching another gate), or a new decision recorded (reject → re-pause at the
    /// same gate, ReapproveOnCascade) each count; polling an unchanged pause writes nothing.
    /// </summary>
    internal static bool IsGateTransition(TestRunDocument doc, TestRunOutcome outcome) =>
        doc.Status != TestRunStatus.PausedAtGate
        || doc.PausedAtGateId != outcome.PausedAtGateId
        || doc.GateDecisions.Count != outcome.GateDecisions.Count;

    private static string ExtractEngagementId(string executionId) =>
        executionId.Contains("::", StringComparison.Ordinal) ? executionId[..executionId.IndexOf("::", StringComparison.Ordinal)] : executionId;

    /// <summary>Resolves the paused gate's <c>GateKind</c> from the draft's definition (S9.38d) — the snapshot alone doesn't carry it. Only called from the gate-pause branch, where <c>PausedAtGateId</c> is always set.</summary>
    private async Task<TestRunOutcome> WithGateKindAsync(TestRunOutcome outcome, string workflowId, CancellationToken ct)
    {
        var draft = await _store.GetDraftAsync(workflowId, ct);
        return draft is null
            ? outcome
            : outcome with { GateKind = TestRunExecution.ResolveGateKind(draft.Definition, outcome.PausedAtGateId) };
    }

    /// <summary>
    /// Projects a still-running snapshot into a live <see cref="TestRunResult"/> — steps completed
    /// so far (with section content), decisions taken so far — without writing anything. Cost
    /// metrics stay zero while live; actuals are read and persisted on transitions only.
    /// </summary>
    private async Task<TestRunResult> BuildLiveResultAsync(TestRunDocument doc, ExecutionSnapshot snapshot, CancellationToken ct)
    {
        var outcome = TestRunExecution.ToOutcome(snapshot);
        return new TestRunResult
        {
            TestRunId = doc.TestRunId,
            DraftRevision = doc.DraftRevision,
            Success = false,
            Status = TestRunStatus.Running,
            NodeSteps = await EnrichWithArtifactContentAsync(doc.TestRunId, outcome.NodeSteps, ct),
            FailureNodeId = null,
            ValidatorFindings = doc.ValidatorFindings,
            CostMetrics = ToCostMetrics(doc.CostMetrics),
            GateDecisions = outcome.GateDecisions,
            CompletedAtUtc = DateTime.UtcNow, // as-of time; the run has not completed
            ErrorMessage = null,
            PausedAtGateId = null,
            GateKind = null,
        };
    }

    private async Task<TestRunHandle> PersistBlockedRunAsync(
        string workflowId, string draftRevision, TestRunGateMode gateMode, DateTime now,
        IReadOnlyList<ValidationFinding> findings, CancellationToken ct)
    {
        var blockedId = $"SANDBOX-{Guid.NewGuid():N}::{workflowId}";
        var outcome = new TestRunOutcome(
            Success: false, NodeSteps: [], ErrorMessage: "Pure-tier validation failed",
            PausedAtGateId: null, GateKind: null, GateDecisions: [], FailureNodeId: null);
        await CreateRunDocumentAsync(workflowId, blockedId, draftRevision, gateMode, now, TestRunStatus.Failed, outcome, now, findings, ct);
        return new TestRunHandle { TestRunId = blockedId, WorkflowId = workflowId, StartedAtUtc = now };
    }

    private async Task<TestRunDocument> CreateRunDocumentAsync(
        string workflowId, string testRunId, string draftRevision, TestRunGateMode gateMode,
        DateTime startedAtUtc, string status, TestRunOutcome outcome, DateTime? completedAtUtc,
        IReadOnlyList<ValidationFinding> findings, CancellationToken ct)
    {
        // S9.29g: a blocked run's testRunId is synthetic — no telemetry was ever staged, so this
        // correctly resolves to all-zero CostMetrics; likewise for a just-started run.
        var costMetrics = await _telemetry.GetCostMetricsAsync(testRunId, ct);

        return await _store.PersistTestRunAsync(new TestRunDocument
        {
            Id = $"{workflowId}:testrun:{Guid.NewGuid()}",
            WorkflowId = workflowId,
            TestRunId = testRunId,
            DraftRevision = draftRevision,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            GateMode = gateMode.ToString(),
            Status = status,
            Success = outcome.Success,
            NodeSteps = outcome.NodeSteps,
            FailureNodeId = outcome.FailureNodeId,
            ValidatorFindings = findings,
            CostMetrics = ToMetricsDictionary(costMetrics),
            GateDecisions = outcome.GateDecisions,
            ErrorMessage = outcome.ErrorMessage,
            PausedAtGateId = outcome.PausedAtGateId,
            GateKind = outcome.GateKind,
            Ttl = TestRunDocument.SandboxRetentionSeconds,
        }, ct);
    }

    /// <summary>
    /// S9.85: transitions update the run's <b>existing</b> document in place (same <c>Id</c>) —
    /// the pre-S9.85 persist minted a fresh document id on every write, so a gate-decision
    /// re-persist silently duplicated the run in the history list.
    /// </summary>
    private async Task<TestRunDocument> UpdateRunDocumentAsync(
        TestRunDocument doc, TestRunOutcome outcome, string status, DateTime? completedAtUtc, CancellationToken ct)
    {
        var costMetrics = await _telemetry.GetCostMetricsAsync(doc.TestRunId, ct);

        return await _store.PersistTestRunAsync(doc with
        {
            Status = status,
            Success = outcome.Success,
            NodeSteps = outcome.NodeSteps,
            FailureNodeId = outcome.FailureNodeId,
            CostMetrics = ToMetricsDictionary(costMetrics),
            GateDecisions = outcome.GateDecisions,
            ErrorMessage = outcome.ErrorMessage,
            PausedAtGateId = outcome.PausedAtGateId,
            GateKind = outcome.GateKind,
            CompletedAtUtc = completedAtUtc,
        }, ct);
    }

    private static System.Collections.ObjectModel.ReadOnlyDictionary<string, string> ToMetricsDictionary(TestRunCostMetrics costMetrics) =>
        new Dictionary<string, string>
        {
            ["total_tokens"] = costMetrics.TotalTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["input_tokens"] = costMetrics.InputTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["output_tokens"] = costMetrics.OutputTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["cache_read_tokens"] = costMetrics.CacheReadTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["cache_write_tokens"] = costMetrics.CacheWriteTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["estimated_cost"] = costMetrics.EstimatedCost.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["budget_exceeded"] = costMetrics.BudgetExceeded ? "true" : "false"
        }.AsReadOnly();

    private static TestRunCostMetrics ToCostMetrics(IReadOnlyDictionary<string, string> metrics) => new()
    {
        TotalTokens = int.Parse(metrics.GetValueOrDefault("total_tokens", "0"), System.Globalization.CultureInfo.InvariantCulture),
        InputTokens = int.Parse(metrics.GetValueOrDefault("input_tokens", "0"), System.Globalization.CultureInfo.InvariantCulture),
        OutputTokens = int.Parse(metrics.GetValueOrDefault("output_tokens", "0"), System.Globalization.CultureInfo.InvariantCulture),
        CacheReadTokens = int.Parse(metrics.GetValueOrDefault("cache_read_tokens", "0"), System.Globalization.CultureInfo.InvariantCulture),
        CacheWriteTokens = int.Parse(metrics.GetValueOrDefault("cache_write_tokens", "0"), System.Globalization.CultureInfo.InvariantCulture),
        EstimatedCost = decimal.Parse(metrics.GetValueOrDefault("estimated_cost", "0"), System.Globalization.CultureInfo.InvariantCulture),
        BudgetExceeded = bool.Parse(metrics.GetValueOrDefault("budget_exceeded", "false")),
    };

    /// <summary>Maps a persisted document to the result contract, enriching steps with live section content (S9.53).</summary>
    private async Task<TestRunResult> ToResultAsync(TestRunDocument doc, CancellationToken ct) => new()
    {
        TestRunId = doc.TestRunId,
        DraftRevision = doc.DraftRevision,
        Success = doc.Success,
        // Legacy fallback mirrors the API list projection exactly: no status + no CompletedAtUtc
        // reads as running (e.g. a doc orphaned before its first checkpoint), else Success decides.
        Status = doc.Status ?? (doc.CompletedAtUtc is null ? TestRunStatus.Running : doc.Success ? TestRunStatus.Completed : TestRunStatus.Failed),
        NodeSteps = await EnrichWithArtifactContentAsync(doc.TestRunId, doc.NodeSteps, ct),
        FailureNodeId = doc.FailureNodeId,
        ValidatorFindings = doc.ValidatorFindings,
        CostMetrics = ToCostMetrics(doc.CostMetrics),
        GateDecisions = doc.GateDecisions,
        CompletedAtUtc = doc.CompletedAtUtc ?? DateTime.UtcNow,
        ErrorMessage = doc.ErrorMessage,
        PausedAtGateId = doc.PausedAtGateId,
        GateKind = doc.GateKind,
    };

    /// <summary>
    /// S9.53: fills each step's <see cref="TestRunNodeStep.OutputContent"/> from the section store
    /// — the step metadata was persisted at run time, the real output is read back live (within
    /// the sandbox TTL window). A step with no section, or whose content has expired, keeps null
    /// content and the UI renders "no output". The execution/engagement ids come from the
    /// <c>SANDBOX-{runId}::{workflowId}</c> test-run id (the section store's own keys).
    /// </summary>
    private async Task<IReadOnlyList<TestRunNodeStep>> EnrichWithArtifactContentAsync(
        string testRunId, IReadOnlyList<TestRunNodeStep> steps, CancellationToken ct)
    {
        var engagementId = testRunId.Split("::", 2)[0];
        var enriched = new List<TestRunNodeStep>(steps.Count);
        foreach (var step in steps)
        {
            var content = step.ArtifactKey is { Length: > 0 } key
                ? await _sections.GetArtifactContentAsync(testRunId, engagementId, key, ct)
                : null;
            enriched.Add(content is null ? step : step with { OutputContent = content });
        }

        return enriched;
    }
}
