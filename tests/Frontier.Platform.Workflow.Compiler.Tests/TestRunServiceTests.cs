using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Storage;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S9.85 (`docs/archive/S9-ASYNC-TESTRUN-PLAN.md`): <see cref="TestRunService"/> is asynchronous —
/// <see cref="TestRunService.StartAsync"/> persists a running document and returns immediately
/// (the S9.38a bounded poll is gone), and the shared reconciler
/// (<see cref="TestRunService.ReconcileAsync"/>) observes the live snapshot, advances auto-approve
/// gates, projects live state, and persists only on transitions. These tests drive that contract
/// with the executor/store mocked — no real orchestration, no timing.
/// </summary>
public sealed class TestRunServiceTests
{
    private readonly Mock<IDefinitionStore> _store = new();
    private readonly Mock<IDefinitionCompiler> _compiler = new();
    private readonly Mock<ITestRunExecutor> _executor = new();
    private readonly Mock<ITestRunTelemetryReader> _telemetry = new();
    private readonly Mock<ITestRunArtifactReader> _sections = new();
    private readonly TestRunService _service;

    private static readonly TestRunCostMetrics ZeroCostMetrics = new()
    {
        TotalTokens = 0, InputTokens = 0, OutputTokens = 0, CacheReadTokens = 0, CacheWriteTokens = 0,
        EstimatedCost = 0m, BudgetExceeded = false,
    };

    public TestRunServiceTests()
    {
        _telemetry.Setup(t => t.GetCostMetricsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(ZeroCostMetrics);
        _service = new TestRunService(_store.Object, _compiler.Object, _executor.Object, _telemetry.Object, _sections.Object);
    }

    private static DefinitionDraftDocument Draft(string workflowId = "wf-test") => new()
    {
        Id = $"{workflowId}:draft",
        WorkflowId = workflowId,
        State = "draft",
        BaseVersion = 1,
        DraftRevision = "rev-1",
        Definition = WorkflowDefinitionFixture.MinimalDefinition(),
        LastEditedBy = "user:test",
        LastEditedUtc = DateTime.UtcNow,
    };

    private static DefinitionDraftDocument DraftWithGate(string workflowId = "wf-test") => Draft(workflowId) with
    {
        Definition = Draft(workflowId).Definition with
        {
            Nodes =
            [
                new HumanGateNode
                {
                    NodeId = "gate-1",
                    GateKind = GateKind.Business,
                    ApproverRoles = ["business-approver"],
                    PromptTemplate = "Approve?",
                    TimeoutMinutes = 0,
                },
            ],
        },
    };

    private static ExecutionSnapshot Snapshot(ExecutionStatus status, string? pausedAtGateId = null, IReadOnlyList<StepCompletion>? completedSteps = null) => new()
    {
        ExecutionId = "SANDBOX-abc::wf-test",
        EngagementId = "SANDBOX-abc",
        WorkflowId = "wf-test",
        DefinitionVersion = 1,
        Sequence = 1,
        Status = status,
        PausedAtGateId = pausedAtGateId,
        Artifacts = new Dictionary<string, ArtifactStatus>(StringComparer.Ordinal),
        CompletedSteps = completedSteps ?? [],
        Decisions = [],
        ApprovedSnapshotRefs = new Dictionary<string, string>(StringComparer.Ordinal),
        CheckpointedAtUtc = DateTime.UtcNow,
    };

    /// <summary>A persisted run document in the given state (defaults to a live running run).</summary>
    private static TestRunDocument RunDoc(
        string testRunId = "SANDBOX-abc::wf-test", string? status = TestRunStatus.Running,
        string gateMode = "AutoApprove", bool success = false, DateTime? completedAtUtc = null,
        string? pausedAtGateId = null, IReadOnlyList<TestRunNodeStep>? nodeSteps = null,
        IReadOnlyList<TestRunGateDecision>? gateDecisions = null) => new()
    {
        Id = "wf-test:testrun:fixed-doc-id",
        WorkflowId = "wf-test",
        TestRunId = testRunId,
        DraftRevision = "rev-1",
        StartedAtUtc = DateTime.UtcNow,
        CompletedAtUtc = completedAtUtc,
        GateMode = gateMode,
        Status = status,
        Success = success,
        NodeSteps = nodeSteps ?? [],
        ValidatorFindings = new List<ValidationFinding>().AsReadOnly(),
        CostMetrics = new Dictionary<string, string> { ["total_tokens"] = "0" }.AsReadOnly(),
        GateDecisions = gateDecisions ?? new List<TestRunGateDecision>().AsReadOnly(),
        ErrorMessage = null,
        PausedAtGateId = pausedAtGateId,
    };

    private void SetupPersistCapture(out Func<TestRunDocument?> persisted)
    {
        TestRunDocument? doc = null;
        _store.Setup(s => s.PersistTestRunAsync(It.IsAny<TestRunDocument>(), It.IsAny<CancellationToken>()))
            .Callback<TestRunDocument, CancellationToken>((d, _) => doc = d)
            .ReturnsAsync((TestRunDocument d, CancellationToken _) => d);
        persisted = () => doc;
    }

    // ── StartAsync (S9.85: persist running, return immediately) ──

    [Fact]
    public async Task StartAsync_NullWorkflowId_Throws()
    {
        var request = new TestRunRequest { SampleInputs = new { }, GateMode = TestRunGateMode.Interactive };

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.StartAsync(null!, request, CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_NullRequest_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.StartAsync("wf-test", null!, CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_MissingDraft_Throws()
    {
        _store.Setup(s => s.GetDraftAsync("wf-test", It.IsAny<CancellationToken>())).ReturnsAsync((DefinitionDraftDocument?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.StartAsync("wf-test", new TestRunRequest { SampleInputs = new { }, GateMode = TestRunGateMode.AutoApprove }, CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_StructuralValidationError_PersistsBlockedRunWithoutStartingExecution()
    {
        _store.Setup(s => s.GetDraftAsync("wf-test", It.IsAny<CancellationToken>())).ReturnsAsync(Draft());
        _compiler.Setup(c => c.ValidateStructural(It.IsAny<WorkflowDefinition>()))
            .Returns([new ValidationFinding("DAG-ness", ValidationSeverity.Error, "cycle detected")]);
        SetupPersistCapture(out var persisted);

        var handle = await _service.StartAsync("wf-test", new TestRunRequest { SampleInputs = new { }, GateMode = TestRunGateMode.AutoApprove }, CancellationToken.None);

        Assert.StartsWith("SANDBOX-", handle.TestRunId, StringComparison.Ordinal);
        Assert.False(persisted()!.Success);
        Assert.Equal(TestRunStatus.Failed, persisted()!.Status); // blocked = terminal from birth
        Assert.NotNull(persisted()!.CompletedAtUtc);
        Assert.Equal("Pure-tier validation failed", persisted()!.ErrorMessage);
        _executor.Verify(e => e.StartAsync(It.IsAny<string>(), It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()), Times.Never);

        // S9.29g (doc 13 §5 advisory evidence): the real blocking findings must attach, not a
        // hardcoded empty list — this is the exact reason the run was blocked.
        var finding = Assert.Single(persisted()!.ValidatorFindings);
        Assert.Equal("DAG-ness", finding.RuleId);

        // No execution ever started under the blocked run's synthetic testRunId, so no
        // telemetry was ever staged for it — CostMetrics correctly resolves to zero without
        // any special-casing at the call site.
        Assert.Equal("0", persisted()!.CostMetrics["total_tokens"]);
    }

    [Fact]
    public async Task StartAsync_ValidDraft_StartsExecutionPersistsRunningAndReturnsImmediately()
    {
        _store.Setup(s => s.GetDraftAsync("wf-test", It.IsAny<CancellationToken>())).ReturnsAsync(Draft());
        _compiler.Setup(c => c.ValidateStructural(It.IsAny<WorkflowDefinition>())).Returns([]);
        _executor.Setup(e => e.StartAsync(It.IsAny<string>(), It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("SANDBOX-abc::wf-test");
        SetupPersistCapture(out var persisted);

        var handle = await _service.StartAsync("wf-test", new TestRunRequest { SampleInputs = new { }, GateMode = TestRunGateMode.AutoApprove }, CancellationToken.None);

        Assert.Equal("SANDBOX-abc::wf-test", handle.TestRunId);
        Assert.Equal("wf-test", handle.WorkflowId);
        Assert.Equal(TestRunStatus.Running, persisted()!.Status);
        Assert.False(persisted()!.Success);
        Assert.Null(persisted()!.CompletedAtUtc);
        Assert.Equal("AutoApprove", persisted()!.GateMode);
        Assert.Equal(TestRunDocument.SandboxRetentionSeconds, persisted()!.Ttl);
        // The async contract: start never observes the run — no snapshot read, no waiting.
        _executor.Verify(e => e.GetSnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_StructuralValidationWarnings_PersistsFindingsWithoutBlocking()
    {
        // Warning-severity findings don't block the run (only Error does) but are still real
        // advisory evidence that must attach to the running document — doc 13 §5.
        _store.Setup(s => s.GetDraftAsync("wf-test", It.IsAny<CancellationToken>())).ReturnsAsync(Draft());
        _compiler.Setup(c => c.ValidateStructural(It.IsAny<WorkflowDefinition>()))
            .Returns([new ValidationFinding("naming", ValidationSeverity.Warning, "consider a clearer name")]);
        _executor.Setup(e => e.StartAsync(It.IsAny<string>(), It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("SANDBOX-abc::wf-test");
        SetupPersistCapture(out var persisted);

        await _service.StartAsync("wf-test", new TestRunRequest { SampleInputs = new { }, GateMode = TestRunGateMode.AutoApprove }, CancellationToken.None);

        Assert.Equal(TestRunStatus.Running, persisted()!.Status);
        var finding = Assert.Single(persisted()!.ValidatorFindings);
        Assert.Equal("naming", finding.RuleId);
        _executor.Verify(e => e.StartAsync(It.IsAny<string>(), It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ReconcileAsync (S9.85: the shared reconciler) ──

    [Fact]
    public async Task ReconcileAsync_NullOrEmptyId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.ReconcileAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ReconcileAsync("", CancellationToken.None));
    }

    [Fact]
    public async Task ReconcileAsync_UnknownRun_ReturnsNull()
    {
        _store.Setup(s => s.GetTestRunAsync("SANDBOX-missing::wf-test", It.IsAny<CancellationToken>())).ReturnsAsync((TestRunDocument?)null);

        Assert.Null(await _service.ReconcileAsync("SANDBOX-missing::wf-test", CancellationToken.None));
    }

    [Fact]
    public async Task ReconcileAsync_TerminalDocument_ReturnsMappedResultWithoutObservingExecution()
    {
        var doc = RunDoc(status: TestRunStatus.Completed, success: true, completedAtUtc: DateTime.UtcNow);
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var result = await _service.ReconcileAsync(doc.TestRunId, CancellationToken.None);

        Assert.True(result!.Success);
        Assert.Equal(TestRunStatus.Completed, result.Status);
        _executor.Verify(e => e.GetSnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.PersistTestRunAsync(It.IsAny<TestRunDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_LegacyDocumentWithCompletedAt_TreatedAsTerminal()
    {
        // Pre-S9.85 documents have no status; their single end-of-run persist set CompletedAtUtc.
        var doc = RunDoc(status: null, success: false, completedAtUtc: DateTime.UtcNow);
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var result = await _service.ReconcileAsync(doc.TestRunId, CancellationToken.None);

        Assert.Equal(TestRunStatus.Failed, result!.Status); // derived from Success=false
        _executor.Verify(e => e.GetSnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_RunningNoCheckpointYet_ReturnsRunningWithoutPersist()
    {
        var doc = RunDoc();
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExecutionSnapshot?)null);

        var before = DateTime.UtcNow;
        var result = await _service.ReconcileAsync(doc.TestRunId, CancellationToken.None);
        var after = DateTime.UtcNow;

        Assert.Equal(TestRunStatus.Running, result!.Status);
        Assert.Empty(result.NodeSteps);
        Assert.InRange(result.CompletedAtUtc, before, after); // as-of time, not a real completion
        _store.Verify(s => s.PersistTestRunAsync(It.IsAny<TestRunDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_RunningSnapshot_ProjectsLiveStepsWithoutPersist()
    {
        var doc = RunDoc();
        var step = new StepCompletion
        {
            NodeId = "node-1", NodeType = NodeType.AgentTask, CorrelationId = "c1", ArtifactKey = "scope",
            OutputContractType = "Out", OutputHash = "hash", RetryCount = 0, CompletedAtUtc = DateTime.UtcNow,
        };
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.Running, completedSteps: [step]));
        _sections.Setup(x => x.GetArtifactContentAsync(doc.TestRunId, "SANDBOX-abc", "scope", It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"scope\":\"live\"}");

        var result = await _service.ReconcileAsync(doc.TestRunId, CancellationToken.None);

        Assert.Equal(TestRunStatus.Running, result!.Status);
        var live = Assert.Single(result.NodeSteps);
        Assert.Equal("node-1", live.NodeId);
        Assert.Equal("{\"scope\":\"live\"}", live.OutputContent); // live steps carry real output too (S9.53 enrichment)
        _store.Verify(s => s.PersistTestRunAsync(It.IsAny<TestRunDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_AutoApprovePausedAtGate_RaisesApprovalWithoutPersist()
    {
        // Advance (policy): the reconciler consumes an auto-approve gate pause — one gate per
        // invocation; the caller's next observation sees post-gate progress.
        var doc = RunDoc(gateMode: "AutoApprove");
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.PausedAtGate, pausedAtGateId: "gate-1"));

        var result = await _service.ReconcileAsync(doc.TestRunId, CancellationToken.None);

        _executor.Verify(e => e.RaiseGateDecisionAsync(doc.TestRunId, "gate-1", TestRunService.AutoApproveApproverId, DecisionKind.Approve, null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(TestRunStatus.Running, result!.Status); // the pause is being consumed, not surfaced
        _store.Verify(s => s.PersistTestRunAsync(It.IsAny<TestRunDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_InteractivePausedAtGate_PersistsPausedTransitionWithGateKind()
    {
        var doc = RunDoc(gateMode: "Interactive");
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _store.Setup(s => s.GetDraftAsync("wf-test", It.IsAny<CancellationToken>())).ReturnsAsync(DraftWithGate());
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.PausedAtGate, pausedAtGateId: "gate-1"));
        SetupPersistCapture(out var persisted);

        var result = await _service.ReconcileAsync(doc.TestRunId, CancellationToken.None);

        Assert.Equal(TestRunStatus.PausedAtGate, persisted()!.Status);
        Assert.Equal("gate-1", persisted()!.PausedAtGateId);
        Assert.Equal(GateKind.Business.Name, persisted()!.GateKind);
        Assert.Null(persisted()!.CompletedAtUtc); // paused is not completed
        Assert.Equal(doc.Id, persisted()!.Id);    // update-in-place, never a duplicate document
        Assert.Equal(TestRunStatus.PausedAtGate, result!.Status);
        _executor.Verify(e => e.RaiseGateDecisionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DecisionKind>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_UnchangedGatePause_DoesNotRepersist()
    {
        // Persist-on-transition: polling an unchanged pause writes nothing (a viewer poll racing
        // the S9.86 sweep must be write-idempotent).
        var doc = RunDoc(gateMode: "Interactive", status: TestRunStatus.PausedAtGate, pausedAtGateId: "gate-1");
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _store.Setup(s => s.GetDraftAsync("wf-test", It.IsAny<CancellationToken>())).ReturnsAsync(DraftWithGate());
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.PausedAtGate, pausedAtGateId: "gate-1"));

        var result = await _service.ReconcileAsync(doc.TestRunId, CancellationToken.None);

        Assert.Equal(TestRunStatus.PausedAtGate, result!.Status);
        _store.Verify(s => s.PersistTestRunAsync(It.IsAny<TestRunDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_RepauseWithNewDecision_RepersistsTransition()
    {
        // Reject → rollback cascade → re-pause at the same gate (ReapproveOnCascade): the gate id
        // is unchanged but a new decision is on the snapshot — that's a transition.
        var doc = RunDoc(gateMode: "Interactive", status: TestRunStatus.PausedAtGate, pausedAtGateId: "gate-1");
        var decision = new HitlDecision
        {
            GateId = "gate-1", RequestId = "req-1", ApproverId = TestRunService.DesignerApproverId,
            Kind = DecisionKind.Reject, DecidedAtUtc = DateTime.UtcNow,
        };
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _store.Setup(s => s.GetDraftAsync("wf-test", It.IsAny<CancellationToken>())).ReturnsAsync(DraftWithGate());
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.PausedAtGate, pausedAtGateId: "gate-1") with { Decisions = [decision] });
        SetupPersistCapture(out var persisted);

        await _service.ReconcileAsync(doc.TestRunId, CancellationToken.None);

        var recorded = Assert.Single(persisted()!.GateDecisions);
        Assert.Equal(TestRunGateOutcome.Rejected, recorded.Outcome);
    }

    [Fact]
    public async Task ReconcileAsync_RunningToCompleted_PersistsTerminalInPlaceWithMetrics()
    {
        var doc = RunDoc();
        var step = new StepCompletion
        {
            NodeId = "node-1", NodeType = NodeType.AgentTask, CorrelationId = "c1",
            OutputContractType = "Out", OutputHash = "hash", RetryCount = 0, CompletedAtUtc = DateTime.UtcNow,
        };
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.Completed, completedSteps: [step]));
        _telemetry.Setup(t => t.GetCostMetricsAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(new TestRunCostMetrics
        {
            TotalTokens = 1500, InputTokens = 1000, OutputTokens = 500, CacheReadTokens = 200, CacheWriteTokens = 50,
            EstimatedCost = 0.0234m, BudgetExceeded = false,
        });
        SetupPersistCapture(out var persisted);

        var result = await _service.ReconcileAsync(doc.TestRunId, CancellationToken.None);

        Assert.Equal(TestRunStatus.Completed, persisted()!.Status);
        Assert.True(persisted()!.Success);
        Assert.NotNull(persisted()!.CompletedAtUtc);
        Assert.Equal(doc.Id, persisted()!.Id); // update-in-place — the pre-S9.85 fresh-id persist duplicated the run
        Assert.Equal("1500", persisted()!.CostMetrics["total_tokens"]);
        Assert.Equal("0.0234", persisted()!.CostMetrics["estimated_cost"]);
        Assert.Equal("completed", persisted()!.NodeSteps.Single(s => s.NodeId == "node-1").Status);
        Assert.True(result!.Success);
        Assert.Equal(TestRunStatus.Completed, result.Status);
    }

    [Theory]
    [InlineData(nameof(ExecutionStatus.Failed), "Test-run execution failed.")]
    [InlineData(nameof(ExecutionStatus.Cancelled), "Test-run was cancelled.")]
    public async Task ReconcileAsync_TerminalFailureStatuses_PersistFailed(string statusName, string expectedMessage)
    {
        var status = statusName == nameof(ExecutionStatus.Failed) ? ExecutionStatus.Failed : ExecutionStatus.Cancelled;
        var doc = RunDoc();
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(status));
        SetupPersistCapture(out var persisted);

        var result = await _service.ReconcileAsync(doc.TestRunId, CancellationToken.None);

        Assert.Equal(TestRunStatus.Failed, persisted()!.Status);
        Assert.False(persisted()!.Success);
        Assert.Equal(expectedMessage, persisted()!.ErrorMessage);
        Assert.Equal(TestRunStatus.Failed, result!.Status);
    }

    [Fact]
    public async Task ReconcileAsync_PausedOnFailure_PersistsFailedWithClassification()
    {
        var doc = RunDoc();
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.PausedOnFailure) with { CurrentNodeId = "node-1", FailureClassification = "contract_violation" });
        _telemetry.Setup(t => t.GetCostMetricsAsync(doc.TestRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ZeroCostMetrics with { BudgetExceeded = true }); // e.g. the S9.38b sandbox fence tripped
        SetupPersistCapture(out var persisted);

        await _service.ReconcileAsync(doc.TestRunId, CancellationToken.None);

        Assert.Equal(TestRunStatus.Failed, persisted()!.Status);
        Assert.Contains("contract_violation", persisted()!.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("node-1", persisted()!.FailureNodeId);
        Assert.Equal("true", persisted()!.CostMetrics["budget_exceeded"]);
    }

    // ── GetResultAsync ──

    [Fact]
    public async Task GetResultAsync_TerminalRun_ReturnsMappedResult()
    {
        var doc = RunDoc(status: null, success: true, completedAtUtc: DateTime.UtcNow,
            nodeSteps: [new TestRunNodeStep { NodeId = "node-1", Status = "completed" }]);
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var result = await _service.GetResultAsync(doc.TestRunId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(doc.TestRunId, result.TestRunId);
        Assert.True(result.Success);
        Assert.Equal(TestRunStatus.Completed, result.Status); // legacy doc derives status from Success
        Assert.Single(result.NodeSteps);
    }

    [Fact]
    public async Task GetResultAsync_EnrichesStepsThatHaveArtifactKeysWithStoredContent()
    {
        // S9.53: TestRunDocument persists metadata only; the node's real output is fetched at
        // read time from the section store, keyed by the run's engagement id (testRunId before "::").
        const string testRunId = "SANDBOX-abc123::wf-test";
        var doc = RunDoc(testRunId: testRunId, status: TestRunStatus.Completed, success: true, completedAtUtc: DateTime.UtcNow, nodeSteps:
        [
            new TestRunNodeStep { NodeId = "n-scope", Status = "completed", ArtifactKey = "scope" },
            new TestRunNodeStep { NodeId = "n-gate", Status = "completed", ArtifactKey = null },
        ]);
        _store.Setup(s => s.GetTestRunAsync(testRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _sections.Setup(x => x.GetArtifactContentAsync(testRunId, "SANDBOX-abc123", "scope", It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"scope\":\"audit the ledger\"}");

        var result = await _service.GetResultAsync(testRunId, CancellationToken.None);

        Assert.Equal("{\"scope\":\"audit the ledger\"}", result!.NodeSteps.Single(s => s.NodeId == "n-scope").OutputContent);
        Assert.Null(result.NodeSteps.Single(s => s.NodeId == "n-gate").OutputContent); // no section key → no fetch
        _sections.Verify(x => x.GetArtifactContentAsync(testRunId, "SANDBOX-abc123", "scope", It.IsAny<CancellationToken>()), Times.Once);
        _sections.VerifyNoOtherCalls(); // the sectionless step never hits the store
    }

    [Fact]
    public async Task GetResultAsync_TestRunNotFound_ReturnsNull()
    {
        _store.Setup(s => s.GetTestRunAsync("SANDBOX-missing::wf-test", It.IsAny<CancellationToken>())).ReturnsAsync((TestRunDocument?)null);

        Assert.Null(await _service.GetResultAsync("SANDBOX-missing::wf-test", CancellationToken.None));
    }

    [Fact]
    public async Task GetResultAsync_WithNullTestRunId_ReturnsNull()
    {
        Assert.Null(await _service.GetResultAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task GetResultAsync_WithEmptyTestRunId_ReturnsNull()
    {
        Assert.Null(await _service.GetResultAsync("", CancellationToken.None));
    }

    [Fact]
    public async Task GetResultAsync_ParsesPersistedCostMetrics()
    {
        var doc = RunDoc(status: TestRunStatus.Completed, success: true, completedAtUtc: DateTime.UtcNow) with
        {
            CostMetrics = new Dictionary<string, string>
            {
                ["total_tokens"] = "1500",
                ["input_tokens"] = "1000",
                ["output_tokens"] = "500",
                ["cache_read_tokens"] = "200",
                ["cache_write_tokens"] = "50",
                ["estimated_cost"] = "0.0234",
                ["budget_exceeded"] = "true"
            }.AsReadOnly(),
        };
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var result = await _service.GetResultAsync(doc.TestRunId, CancellationToken.None);

        Assert.Equal(1500, result!.CostMetrics.TotalTokens);
        Assert.Equal(1000, result.CostMetrics.InputTokens);
        Assert.Equal(500, result.CostMetrics.OutputTokens);
        Assert.Equal(200, result.CostMetrics.CacheReadTokens);
        Assert.Equal(50, result.CostMetrics.CacheWriteTokens);
        Assert.Equal(0.0234m, result.CostMetrics.EstimatedCost);
        Assert.True(result.CostMetrics.BudgetExceeded);
    }

    // ── DecideGateAsync (S9.38d/S9.85: raise + reconcile once, no blocking) ──

    [Fact]
    public async Task DecideGateAsync_NullTestRunId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.DecideGateAsync(null!, "gate-1", DecisionKind.Approve, null, CancellationToken.None));
    }

    [Fact]
    public async Task DecideGateAsync_NullGateId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.DecideGateAsync("SANDBOX-abc::wf-test", null!, DecisionKind.Approve, null, CancellationToken.None));
    }

    [Fact]
    public async Task DecideGateAsync_NullDecision_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.DecideGateAsync("SANDBOX-abc::wf-test", "gate-1", null!, null, CancellationToken.None));
    }

    [Fact]
    public async Task DecideGateAsync_UnknownTestRunId_ReturnsNull()
    {
        _store.Setup(s => s.GetTestRunAsync("SANDBOX-abc::wf-test", It.IsAny<CancellationToken>())).ReturnsAsync((TestRunDocument?)null);

        var result = await _service.DecideGateAsync("SANDBOX-abc::wf-test", "gate-1", DecisionKind.Approve, null, CancellationToken.None);

        Assert.Null(result);
        _executor.Verify(e => e.RaiseGateDecisionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DecisionKind>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DecideGateAsync_PausedRun_RaisesDesignerDecisionThenReturnsCurrentState()
    {
        // The decision is raised and the run reconciled ONCE — if the orchestration hasn't
        // processed the event yet the run is still paused; the caller's next poll observes the
        // continuation. No blocking wait exists any more.
        var doc = RunDoc(gateMode: "Interactive", status: TestRunStatus.PausedAtGate, pausedAtGateId: "gate-1");
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _store.Setup(s => s.GetDraftAsync("wf-test", It.IsAny<CancellationToken>())).ReturnsAsync(DraftWithGate());
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.PausedAtGate, pausedAtGateId: "gate-1"));

        var result = await _service.DecideGateAsync(doc.TestRunId, "gate-1", DecisionKind.Approve, "looks good", CancellationToken.None);

        _executor.Verify(e => e.RaiseGateDecisionAsync(doc.TestRunId, "gate-1", TestRunService.DesignerApproverId, DecisionKind.Approve, "looks good", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(TestRunStatus.PausedAtGate, result!.Status); // not yet processed — the poll observes what happens next
    }

    [Fact]
    public async Task DecideGateAsync_OrchestrationAlreadyAdvanced_PersistsTerminalOutcome()
    {
        var doc = RunDoc(gateMode: "Interactive", status: TestRunStatus.PausedAtGate, pausedAtGateId: "gate-1");
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.Completed));
        SetupPersistCapture(out var persisted);

        var result = await _service.DecideGateAsync(doc.TestRunId, "gate-1", DecisionKind.Approve, null, CancellationToken.None);

        Assert.True(persisted()!.Success);
        Assert.Equal(TestRunStatus.Completed, persisted()!.Status);
        Assert.True(result!.Success);
    }

    [Fact]
    public async Task DecideGateAsync_RunPausesAtASecondGate_ResolvesItsGateKindFromTheDraft()
    {
        var doc = RunDoc(gateMode: "Interactive", status: TestRunStatus.PausedAtGate, pausedAtGateId: "gate-0");
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _store.Setup(s => s.GetDraftAsync("wf-test", It.IsAny<CancellationToken>())).ReturnsAsync(DraftWithGate());
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.PausedAtGate, pausedAtGateId: "gate-1"));
        SetupPersistCapture(out var persisted);

        await _service.DecideGateAsync(doc.TestRunId, "gate-0", DecisionKind.Approve, null, CancellationToken.None);

        Assert.Equal("gate-1", persisted()!.PausedAtGateId);
        Assert.Equal(GateKind.Business.Name, persisted()!.GateKind);
    }

    [Fact]
    public async Task DecideGateAsync_DraftNoLongerExists_StillPersistsOutcomeWithoutGateKind()
    {
        var doc = RunDoc(gateMode: "Interactive", status: TestRunStatus.PausedAtGate, pausedAtGateId: "gate-1");
        _store.Setup(s => s.GetTestRunAsync(doc.TestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _store.Setup(s => s.GetDraftAsync("wf-test", It.IsAny<CancellationToken>())).ReturnsAsync((DefinitionDraftDocument?)null);
        _executor.Setup(e => e.GetSnapshotAsync(doc.TestRunId, "SANDBOX-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.PausedAtGate, pausedAtGateId: "gate-2"));
        SetupPersistCapture(out var persisted);

        await _service.DecideGateAsync(doc.TestRunId, "gate-1", DecisionKind.Reject, null, CancellationToken.None);

        Assert.Equal("gate-2", persisted()!.PausedAtGateId);
        Assert.Null(persisted()!.GateKind);
    }

    [Fact]
    public async Task DecideGateAsync_MalformedTestRunIdWithoutSeparator_StillResolves()
    {
        // Defensive: ExtractEngagementId falls back to the whole string when "::" is absent
        // (a malformed testRunId shouldn't crash the endpoint, even though every real one
        // minted by ITestRunExecutor.StartAsync always contains "::").
        var doc = RunDoc(testRunId: "malformed-id", gateMode: "Interactive", status: TestRunStatus.PausedAtGate, pausedAtGateId: "gate-1");
        _store.Setup(s => s.GetTestRunAsync("malformed-id", It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _executor.Setup(e => e.GetSnapshotAsync("malformed-id", "malformed-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(ExecutionStatus.Completed));
        SetupPersistCapture(out var persisted);

        await _service.DecideGateAsync("malformed-id", "gate-1", DecisionKind.Approve, null, CancellationToken.None);

        Assert.True(persisted()!.Success);
    }

    // ── Pure state helpers ──

    [Theory]
    [InlineData(TestRunStatus.Completed, false, true)]
    [InlineData(TestRunStatus.Failed, false, true)]
    [InlineData(TestRunStatus.Running, false, false)]
    [InlineData(TestRunStatus.PausedAtGate, false, false)]
    [InlineData(null, true, true)]   // legacy doc, end-of-run persist happened
    [InlineData(null, false, false)] // legacy doc mid-flight (never occurred in practice, but safe)
    public void IsTerminalDocument_CoversStatusAndLegacyDerivation(string? status, bool hasCompletedAt, bool expected)
    {
        var doc = RunDoc(status: status, completedAtUtc: hasCompletedAt ? DateTime.UtcNow : null);

        Assert.Equal(expected, TestRunService.IsTerminalDocument(doc));
    }

    [Theory]
    [InlineData("AutoApprove", true)]
    [InlineData("autoapprove", true)] // case-insensitive parse
    [InlineData("Interactive", false)]
    [InlineData("garbage", false)]
    public void IsAutoApprove_ParsesThePersistedGateMode(string gateMode, bool expected)
    {
        Assert.Equal(expected, TestRunService.IsAutoApprove(RunDoc(gateMode: gateMode)));
    }
}
