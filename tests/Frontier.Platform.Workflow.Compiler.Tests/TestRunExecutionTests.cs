using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>S9.38a: pure mapping from an <see cref="ExecutionSnapshot"/> to a sandbox test-run outcome.</summary>
public sealed class TestRunExecutionTests
{
    private static ExecutionSnapshot Snapshot(ExecutionStatus status, string? pausedAtGateId = null, IReadOnlyList<StepCompletion>? completedSteps = null, IReadOnlyList<HitlDecision>? decisions = null, string? currentNodeId = null, string? failureClassification = null, IReadOnlyList<string>? skippedNodeIds = null) => new()
    {
        ExecutionId = "SANDBOX-abc::wf-test",
        EngagementId = "SANDBOX-abc",
        WorkflowId = "wf-test",
        DefinitionVersion = 1,
        Sequence = 1,
        Status = status,
        CurrentNodeId = currentNodeId,
        PausedAtGateId = pausedAtGateId,
        Artifacts = new Dictionary<string, ArtifactStatus>(StringComparer.Ordinal),
        CompletedSteps = completedSteps ?? [],
        Decisions = decisions ?? [],
        ApprovedSnapshotRefs = new Dictionary<string, string>(StringComparer.Ordinal),
        CheckpointedAtUtc = DateTime.UtcNow,
        FailureClassification = failureClassification,
        SkippedNodeIds = skippedNodeIds,
    };

    private static HitlDecision Decision(string gateId, DecisionKind kind, string? notes = null) => new()
    {
        GateId = gateId,
        RequestId = $"SANDBOX-abc::wf-test:{gateId}:0",
        ApproverId = "system:sandbox-auto-approve",
        Kind = kind,
        Notes = notes,
        DecidedAtUtc = DateTime.UtcNow,
    };

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("paused_on_failure")] // S9.45: terminal for polling purposes - nothing ever writes a further snapshot after it.
    public void IsTerminal_TerminalStatuses_ReturnsTrue(string statusName)
    {
        var status = ExecutionStatus.List.Single(s => s.Name == statusName);

        Assert.True(TestRunExecution.IsTerminal(status));
    }

    [Theory]
    [InlineData("running")]
    [InlineData("paused_at_gate")]
    public void IsTerminal_NonTerminalStatuses_ReturnsFalse(string statusName)
    {
        var status = ExecutionStatus.List.Single(s => s.Name == statusName);

        Assert.False(TestRunExecution.IsTerminal(status));
    }

    [Fact]
    public void BuildNodeSteps_CompletedSteps_CarryTheNodeMetadata()
    {
        // S9.53: the per-step row carries the snapshot's real metadata (content is fetched later).
        var step = new StepCompletion
        {
            NodeId = "node-1", NodeType = NodeType.AgentTask, CorrelationId = "c1", ArtifactKey = "scope",
            OutputContractType = "SummaryArtifact", OutputHash = "hash", RetryCount = 2, CompletedAtUtc = DateTime.UtcNow,
        };

        var steps = TestRunExecution.BuildNodeSteps(Snapshot(ExecutionStatus.Completed, completedSteps: [step]));

        var only = Assert.Single(steps);
        Assert.Equal("node-1", only.NodeId);
        Assert.Equal("completed", only.Status);
        Assert.Equal("agent_task", only.NodeType);
        Assert.Equal("SummaryArtifact", only.OutputContractType);
        Assert.Equal("scope", only.ArtifactKey);
        Assert.Equal(2, only.RetryCount);
        Assert.Null(only.OutputContent); // filled later by TestRunService from the section store
    }

    [Fact]
    public void BuildNodeSteps_DecisionStep_CarriesItsRoutingOutcome()
    {
        // S13.31 (doc 19 A4-R4): a decision produces no payload — the branch it selected IS
        // its result, so the row must carry it or the panel reads as "no output" (the S13.7j
        // legibility gap Mark found: nothing was emitted for the decision step).
        var decision = new StepCompletion
        {
            NodeId = "cost_decision", NodeType = NodeType.Decision, CorrelationId = "c1",
            OutputContractType = string.Empty, OutputHash = string.Empty, RetryCount = 0,
            CompletedAtUtc = DateTime.UtcNow, SelectedBranchNodeId = "merge",
        };

        var only = Assert.Single(TestRunExecution.BuildNodeSteps(Snapshot(ExecutionStatus.Completed, completedSteps: [decision])));

        Assert.Equal("decision", only.NodeType);
        Assert.Equal("merge", only.SelectedBranchNodeId);
        Assert.Equal("completed", only.Status);
    }

    [Fact]
    public void BuildNodeSteps_SkippedNodes_AppearAsSkippedRows()
    {
        // Without these rows a skipped node simply vanishes from the feed, which reads like a
        // bug rather than routing (ADR-5 D6).
        var completed = new StepCompletion
        {
            NodeId = "cost_decision", NodeType = NodeType.Decision, CorrelationId = "c1",
            OutputContractType = string.Empty, OutputHash = string.Empty, RetryCount = 0,
            CompletedAtUtc = DateTime.UtcNow, SelectedBranchNodeId = "merge",
        };

        var steps = TestRunExecution.BuildNodeSteps(Snapshot(ExecutionStatus.Completed,
            completedSteps: [completed], skippedNodeIds: ["review_gate", "escalate"]));

        Assert.Equal(3, steps.Count);
        Assert.Equal(["cost_decision", "review_gate", "escalate"], steps.Select(s => s.NodeId));
        Assert.Equal(["completed", "skipped", "skipped"], steps.Select(s => s.Status));
        // A skipped node never ran: no contract, no hash, no duration to report.
        var skipped = steps[1];
        Assert.Null(skipped.OutputContractType);
        Assert.Null(skipped.DurationMs);
        Assert.Null(skipped.SelectedBranchNodeId);
    }

    [Fact]
    public void BuildNodeSteps_NoSkippedNodes_AddsNoRows()
    {
        var step = new StepCompletion
        {
            NodeId = "node-1", NodeType = NodeType.AgentTask, CorrelationId = "c1", ArtifactKey = "scope",
            OutputContractType = "SummaryArtifact", OutputHash = "hash", RetryCount = 0, CompletedAtUtc = DateTime.UtcNow,
        };

        var steps = TestRunExecution.BuildNodeSteps(Snapshot(ExecutionStatus.Completed, completedSteps: [step]));

        Assert.Single(steps);
        Assert.Null(steps[0].SelectedBranchNodeId);
    }

    [Fact]
    public void BuildNodeSteps_DerivesPerStepDurationFromCompletionDeltas()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        StepCompletion At(string id, DateTime at) => new()
        {
            NodeId = id, NodeType = NodeType.AgentTask, CorrelationId = id, OutputContractType = "Out",
            OutputHash = "h", RetryCount = 0, CompletedAtUtc = at,
        };

        var steps = TestRunExecution.BuildNodeSteps(Snapshot(ExecutionStatus.Completed,
            completedSteps: [At("a", t0), At("b", t0.AddMilliseconds(1500))]));

        Assert.Null(steps[0].DurationMs); // first step has no predecessor
        Assert.Equal(1500, steps[1].DurationMs);
    }

    [Fact]
    public void BuildNodeSteps_NoCompletedSteps_ReturnsEmpty()
    {
        Assert.Empty(TestRunExecution.BuildNodeSteps(Snapshot(ExecutionStatus.Running)));
    }

    [Fact]
    public void ToOutcome_PlainFailure_SetsFailureNodeIdToLastCompletedNode()
    {
        // C-35 (S9.53): the plain-failure canvas-link anchor is the snapshot's current node.
        var failed = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.PausedOnFailure, currentNodeId: "gen-scope"));
        var completed = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.Completed, currentNodeId: "gen-scope"));
        var atGate = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.PausedAtGate, pausedAtGateId: "g1", currentNodeId: "gen-scope"));

        Assert.Equal("gen-scope", failed.FailureNodeId);
        Assert.Null(completed.FailureNodeId); // success has no failure anchor
        Assert.Null(atGate.FailureNodeId);    // a gate pause isn't a plain failure
    }

    [Fact]
    public void ToOutcome_Completed_SuccessWithNoErrorMessage()
    {
        var outcome = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.Completed));

        Assert.True(outcome.Success);
        Assert.Null(outcome.ErrorMessage);
    }

    [Fact]
    public void ToOutcome_Failed_FailureWithMessage()
    {
        var outcome = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.Failed));

        Assert.False(outcome.Success);
        Assert.Equal("Test-run execution failed.", outcome.ErrorMessage);
    }

    [Fact]
    public void ToOutcome_Cancelled_FailureWithMessage()
    {
        var outcome = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.Cancelled));

        Assert.False(outcome.Success);
        Assert.Equal("Test-run was cancelled.", outcome.ErrorMessage);
    }

    [Fact]
    public void ToOutcome_PausedAtGate_FailureNamingTheGate()
    {
        var outcome = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.PausedAtGate, pausedAtGateId: "gate-1"));

        Assert.False(outcome.Success);
        Assert.Contains("gate-1", outcome.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ToOutcome_PausedOnFailure_FailureNamingNodeAndClassification()
    {
        var outcome = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.PausedOnFailure, currentNodeId: "gen-scope", failureClassification: "contract_violation"));

        Assert.False(outcome.Success);
        Assert.Contains("gen-scope", outcome.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("contract_violation", outcome.ErrorMessage, StringComparison.Ordinal);
        Assert.Null(outcome.PausedAtGateId);
    }

    // S9.85: the TimedOut outcome is gone — StartAsync no longer bounded-polls, so a run can
    // never "not reach a terminal state within the sandbox wait window".

    // ── GateDecisions (S9.29g, doc 13 §5 "gate decisions taken") ──

    [Fact]
    public void ToOutcome_NoDecisions_ReturnsEmptyGateDecisions()
    {
        var outcome = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.Completed));

        Assert.Empty(outcome.GateDecisions);
    }

    [Fact]
    public void ToOutcome_ApproveDecision_MapsToApprovedGateDecision()
    {
        var outcome = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.Completed, decisions: [Decision("gate-1", DecisionKind.Approve, "looks good")]));

        var decision = Assert.Single(outcome.GateDecisions);
        Assert.Equal("gate-1", decision.GateId);
        Assert.Equal(TestRunGateOutcome.Approved, decision.Outcome);
        Assert.Equal("looks good", decision.Note);
    }

    [Fact]
    public void ToOutcome_RejectDecision_MapsToRejectedGateDecision()
    {
        var outcome = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.PausedAtGate, pausedAtGateId: "gate-1", decisions: [Decision("gate-1", DecisionKind.Reject)]));

        var decision = Assert.Single(outcome.GateDecisions);
        Assert.Equal(TestRunGateOutcome.Rejected, decision.Outcome);
    }

    [Fact]
    public void ToOutcome_EscalateDecision_CollapsesToRejectedGateDecision()
    {
        // TestRunGateOutcome is binary (approved/rejected) — sandbox test-runs have no
        // escalation-routing concept (doc 13 §5), so Escalate collapses to Rejected.
        var outcome = TestRunExecution.ToOutcome(Snapshot(ExecutionStatus.Completed, decisions: [Decision("gate-1", DecisionKind.Escalate)]));

        var decision = Assert.Single(outcome.GateDecisions);
        Assert.Equal(TestRunGateOutcome.Rejected, decision.Outcome);
    }

    [Fact]
    public void ToOutcome_MultipleDecisions_MapsEachInOrder()
    {
        var outcome = TestRunExecution.ToOutcome(Snapshot(
            ExecutionStatus.Completed,
            decisions: [Decision("gate-1", DecisionKind.Approve), Decision("gate-2", DecisionKind.Reject)]));

        Assert.Equal(2, outcome.GateDecisions.Count);
        Assert.Equal("gate-1", outcome.GateDecisions[0].GateId);
        Assert.Equal("gate-2", outcome.GateDecisions[1].GateId);
    }

    // ── ResolveGateKind (S9.38d) ──

    [Fact]
    public void ResolveGateKind_NullGateId_ReturnsNull()
    {
        var definition = WorkflowDefinitionFixture.MinimalDefinition();

        Assert.Null(TestRunExecution.ResolveGateKind(definition, null));
    }

    [Fact]
    public void ResolveGateKind_KnownGateId_ReturnsItsGateKindName()
    {
        var definition = WorkflowDefinitionFixture.MinimalDefinition() with
        {
            Nodes = [new HumanGateNode { NodeId = "gate-1", GateKind = GateKind.Technical, ApproverRoles = ["r"], PromptTemplate = "p", TimeoutMinutes = 0 }],
        };

        Assert.Equal("technical", TestRunExecution.ResolveGateKind(definition, "gate-1"));
    }

    [Fact]
    public void ResolveGateKind_UnknownGateId_ReturnsNull()
    {
        var definition = WorkflowDefinitionFixture.MinimalDefinition();

        Assert.Null(TestRunExecution.ResolveGateKind(definition, "no-such-gate"));
    }
}
