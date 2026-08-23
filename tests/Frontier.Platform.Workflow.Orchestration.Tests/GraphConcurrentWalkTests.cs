using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;
using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.7i (ADR-5) tests for the ready-set scheduler: independent branches run
/// concurrently, gates are barriers, a permanent failure lets in-flight siblings finish
/// before the walk pauses attributed, and scheduling stays deterministic. Deferrable
/// activity handlers (<see cref="FakeTaskOrchestrationContext.AsyncActivityHandlers"/>)
/// hold branches open so tests control completion order explicitly.
/// </summary>
public sealed class GraphConcurrentWalkTests
{
    private static readonly FakeResiliencePolicyProvider PolicyProvider = new();

    [Fact]
    public async Task FanOut_BothBranchesAreInFlightTogether()
    {
        var harness = new WalkHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["b-booking", "b-ticket"]);

        var walk = harness.StartWalk();

        // The entry completed inline; both branches were scheduled before either finished.
        Assert.Equal(["a-entry", "b-booking", "b-ticket"], harness.StartedNodes);
        Assert.False(walk.IsCompleted);

        harness.CompleteNode("b-booking");
        harness.CompleteNode("b-ticket");
        var state = await walk;

        Assert.Equal(["a-entry", "b-booking", "b-ticket", "c-join"], harness.StartedNodes);
        Assert.Equal(4, state.CompletedSteps.Count);
    }

    [Fact]
    public async Task FanOut_OutOfOrderCompletion_RecordsBothBranchesAndStaysMonotonic()
    {
        var harness = new WalkHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["b-booking", "b-ticket"]);
        var walk = harness.StartWalk();

        // Complete the lexicographically *later* branch first.
        harness.CompleteNode("b-ticket");
        harness.CompleteNode("b-booking");
        var state = await walk;

        Assert.Equal(["a-entry", "b-ticket", "b-booking", "c-join"], state.CompletedSteps.Select(step => step.NodeId));
        Assert.Equal(ArtifactStatus.Draft, state.ArtifactStatuses["booking"]);
        Assert.Equal(ArtifactStatus.Draft, state.ArtifactStatuses["ticket"]);
        // One checkpoint per node completion, strictly monotonic sequence (doc 02 §5).
        Assert.Equal([1, 2, 3, 4], harness.Snapshots.Select(snapshot => snapshot.Sequence));
        Assert.All(harness.Snapshots, snapshot => Assert.Equal(ExecutionStatus.Running, snapshot.Status));
    }

    [Fact]
    public async Task FanOut_BranchFails_SiblingFinishesThenWalkPausesAttributed()
    {
        var harness = new WalkHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["b-booking", "b-ticket"]);
        var walk = harness.StartWalk();

        harness.FailNode("b-booking", new ContractViolationException(nameof(AgentTaskActivityInput), ["output failed validation."]));
        Assert.False(walk.IsCompleted); // sibling still in flight — the walk drains, it does not fail fast
        harness.CompleteNode("b-ticket");

        var thrown = await Assert.ThrowsAsync<ContractViolationException>(() => walk);

        Assert.Contains("output failed validation", thrown.Message, StringComparison.Ordinal);
        // The sibling's work was kept and checkpointed; the join never started (ADR-5 D4).
        Assert.DoesNotContain("c-join", harness.StartedNodes);
        var final = harness.Snapshots[^1];
        Assert.Equal(ExecutionStatus.PausedOnFailure, final.Status);
        Assert.Equal("b-booking", final.CurrentNodeId);
        Assert.Equal("contract_violation", final.FailureClassification);
        Assert.Contains(final.CompletedSteps, step => step.NodeId == "b-ticket");
    }

    [Fact]
    public async Task FanOut_FailureBeforeSiblingCompletes_SiblingResultStillRecorded()
    {
        var harness = new WalkHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["b-booking", "b-ticket"]);
        var walk = harness.StartWalk();

        // Failure arrives first; the sibling completes afterwards and must still checkpoint.
        harness.FailNode("b-ticket", new BudgetExceededException("invocation budget exhausted."));
        harness.CompleteNode("b-booking");

        await Assert.ThrowsAsync<BudgetExceededException>(() => walk);

        var final = harness.Snapshots[^1];
        Assert.Equal("b-ticket", final.CurrentNodeId);
        Assert.Equal("guardrail", final.FailureClassification);
        Assert.Contains(final.CompletedSteps, step => step.NodeId == "b-booking");
        Assert.DoesNotContain("c-join", harness.StartedNodes);
    }

    [Fact]
    public async Task GateBarrier_GateWaitsForUnrelatedInFlightBranch()
    {
        var harness = new WalkHarness(OrchestrationFixtures.ParallelBranchesGateOnOne(), deferred: ["b-independent"]);
        harness.Context.ExternalEvents[GraphOrchestratorSteps.GateEventName("gate-scope")] = Decision(DecisionKind.Approve);
        var walk = harness.StartWalk();

        // a-reviewed completed inline and the gate is ready — but b-independent is still
        // running, so the barrier must hold the gate closed (ADR-5 D2).
        Assert.Empty(harness.GateOpenings);

        harness.CompleteNode("b-independent");
        var state = await walk;

        Assert.Equal("gate-scope", Assert.Single(harness.GateOpenings).GateId);
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["scope"]);
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["approach"]);
    }

    [Fact]
    public async Task Scheduling_IsDeterministicAcrossIdenticalRuns()
    {
        var first = new WalkHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["b-booking", "b-ticket"]);
        var second = new WalkHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["b-booking", "b-ticket"]);

        var firstWalk = first.StartWalk();
        first.CompleteNode("b-ticket");
        first.CompleteNode("b-booking");
        await firstWalk;

        var secondWalk = second.StartWalk();
        second.CompleteNode("b-ticket");
        second.CompleteNode("b-booking");
        await secondWalk;

        // Same completion history → identical schedule and identical checkpoints (the
        // unit-scope replay-stability proof; the live gate tests are the integration half).
        Assert.Equal(first.StartedNodes, second.StartedNodes);
        Assert.Equal(
            first.Snapshots.Select(snapshot => (snapshot.Sequence, snapshot.CurrentNodeId, snapshot.Status.Name)),
            second.Snapshots.Select(snapshot => (snapshot.Sequence, snapshot.CurrentNodeId, snapshot.Status.Name)));
    }

    [Fact]
    public async Task CyclicDefinition_ThrowsContractViolation()
    {
        var harness = new WalkHarness(OrchestrationFixtures.TwoNodeCycle(), deferred: []);

        var exception = await Assert.ThrowsAsync<ContractViolationException>(() => harness.StartWalk());

        Assert.Contains("cycle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CascadeRerun_MintsPerNodeOccurrenceCorrelationIds()
    {
        // The correlation id's third segment is the per-node occurrence (ADR-5 D5): a
        // cascade re-run of the same node gets occurrence 1, not a shared step count.
        var harness = new WalkHarness(OrchestrationFixtures.ThreeArtifactChain(), deferred: []);
        var state = await harness.StartWalk();

        await GraphOrchestratorSteps.RegenerateDownstreamAsync(harness.Context, OrchestrationFixtures.Input(harness.Definition), state, ["approach"], PolicyProvider);

        var approachRuns = state.CompletedSteps.Where(step => step.NodeId == "approach-agent").ToList();
        Assert.Equal(2, approachRuns.Count);
        Assert.EndsWith("::approach-agent::0", approachRuns[0].CorrelationId, StringComparison.Ordinal);
        Assert.EndsWith("::approach-agent::1", approachRuns[1].CorrelationId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BothBranchesFail_FirstFailureWinsAttribution()
    {
        var harness = new WalkHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["b-booking", "b-ticket"]);
        var walk = harness.StartWalk();

        harness.FailNode("b-ticket", new ContractViolationException(nameof(AgentTaskActivityInput), ["first failure."]));
        harness.FailNode("b-booking", new BudgetExceededException("second failure."));

        var thrown = await Assert.ThrowsAsync<ContractViolationException>(() => walk);

        // The first observed fault wins; the sibling's later fault is absorbed (ADR-5 D4).
        Assert.Contains("first failure", thrown.Message, StringComparison.Ordinal);
        var final = harness.Snapshots[^1];
        Assert.Equal("b-ticket", final.CurrentNodeId);
        Assert.Equal("contract_violation", final.FailureClassification);
    }

    [Fact]
    public async Task FailureWithNewlyReadyNode_DoesNotScheduleIt()
    {
        // ADR-5 D4: after the first failure no NEW node starts — here the join depends
        // only on the surviving branch, so it becomes ready during the drain and must
        // still be refused.
        var definition = OrchestrationFixtures.FanOutJoin();
        definition = definition with
        {
            Edges =
            [
                new() { FromNodeId = "a-entry", ToNodeId = "b-booking", Kind = EdgeKind.Control },
                new() { FromNodeId = "a-entry", ToNodeId = "b-ticket", Kind = EdgeKind.Control },
                new() { FromNodeId = "b-ticket", ToNodeId = "c-join", Kind = EdgeKind.Control },
            ],
        };
        var harness = new WalkHarness(definition, deferred: ["b-booking", "b-ticket"]);
        var walk = harness.StartWalk();

        harness.FailNode("b-booking", new ContractViolationException(nameof(AgentTaskActivityInput), ["boom."]));
        harness.CompleteNode("b-ticket");

        await Assert.ThrowsAsync<ContractViolationException>(() => walk);

        Assert.DoesNotContain("c-join", harness.StartedNodes);
        Assert.Equal("b-booking", harness.Snapshots[^1].CurrentNodeId);
    }

    [Fact]
    public void GraphWalk_TakeReadyGateWhenQuiesced_EmptyReadySet_ReturnsNull()
    {
        // Defensive contract of the walk bookkeeping itself: quiescence with nothing
        // ready yields no gate (unreachable from the scheduler loop, guarded regardless).
        var walk = GraphWalk.Create(OrchestrationFixtures.FanOutJoin());
        walk.TakeReadyAgentNodes();

        Assert.Null(walk.TakeReadyGateWhenQuiesced());
    }

    [Fact]
    public void GraphWalk_DrainFinished_ReturnsFinishedTasksInNodeIdOrder()
    {
        var walk = GraphWalk.Create(OrchestrationFixtures.FanOutJoin());
        walk.Running["b-ticket"] = Task.CompletedTask;
        walk.Running["b-booking"] = Task.CompletedTask;
        walk.Running["c-join"] = new TaskCompletionSource().Task;

        var finished = walk.DrainFinished();

        Assert.Equal(["b-booking", "b-ticket"], finished.Select(pair => pair.Key));
        Assert.Equal(["c-join"], walk.Running.Keys);
    }

    [Fact]
    public void GraphWalk_ThrowIfIncomplete_AfterFailure_DoesNotThrow()
    {
        // A failed walk is legitimately incomplete — the failure path owns the throw
        // (ThrowIfFailedAsync); the cycle alarm must stay silent.
        var walk = GraphWalk.Create(OrchestrationFixtures.FanOutJoin());
        walk.Fail("b-booking", new InvalidOperationException("boom"));

        walk.ThrowIfIncomplete();
    }

    [Fact]
    public void ClassifyFailure_TaskFailedException_WalksTheDetailsChain()
    {
        // Real DTF activity failures arrive as TaskFailedException; the details chain is
        // walked level by level (each IsCausedBy checks only itself).
        var contractViolation = new TaskFailureDetails(typeof(ContractViolationException).FullName!, "bad output", null, null, null);
        var wrapped = new TaskFailureDetails("WrapperException", "outer", null, contractViolation, null);
        var budget = new TaskFailureDetails(typeof(BudgetExceededException).FullName!, "over budget", null, null, null);

        Assert.Equal("contract_violation", GraphOrchestratorSteps.ClassifyFailureDetails(contractViolation));
        Assert.Equal("contract_violation", GraphOrchestratorSteps.ClassifyFailureDetails(wrapped));
        Assert.Equal("guardrail", GraphOrchestratorSteps.ClassifyFailureDetails(budget));
        Assert.Equal("unclassified", GraphOrchestratorSteps.ClassifyFailureDetails(new TaskFailureDetails("Other", "?", null, null, null)));
        Assert.Equal("unclassified", GraphOrchestratorSteps.ClassifyFailureDetails(null));
    }

    [Fact]
    public void UnwrapTaskException_EmptyAggregate_ReturnsTheAggregateItself()
    {
        var aggregate = new AggregateException();

        Assert.Same(aggregate, GraphOrchestratorSteps.UnwrapTaskException(aggregate));
    }

    [Fact]
    public void ClassifyFailure_MapsTheDocTenTaxonomy()
    {
        Assert.Equal("contract_violation", GraphOrchestratorSteps.ClassifyFailure(new ContractViolationException("X", ["bad"])));
        Assert.Equal("guardrail", GraphOrchestratorSteps.ClassifyFailure(new BudgetExceededException("over")));
        Assert.Equal("unclassified", GraphOrchestratorSteps.ClassifyFailure(new InvalidOperationException("?")));
        Assert.Equal("guardrail", GraphOrchestratorSteps.ClassifyFailure(
            new TaskFailedException("AgentTaskActivity", 1, new TaskFailureDetails(typeof(BudgetExceededException).FullName!, "over", null, null, null))));
    }

    private static HitlDecision Decision(DecisionKind kind) => new()
    {
        GateId = "gate-scope",
        RequestId = "eng-1::wf-gate-barrier:gate-scope:0",
        ApproverId = "user:approver-1",
        Kind = kind,
        DecidedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>
    /// Drives one walk with per-node deferrable agent activities: nodes named in
    /// <c>deferred</c> stay in flight until the test releases them via
    /// <see cref="CompleteNode"/>/<see cref="FailNode"/>; everything else completes inline.
    /// </summary>
    private sealed class WalkHarness
    {
        private readonly HashSet<string> _deferred;
        private readonly Dictionary<string, TaskCompletionSource<object>> _pending = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AgentTaskActivityInput> _inputs = new(StringComparer.Ordinal);

        public WalkHarness(WorkflowDefinition definition, IReadOnlyList<string> deferred)
        {
            Definition = definition;
            _deferred = new HashSet<string>(deferred, StringComparer.Ordinal);
            Context = new FakeTaskOrchestrationContext();
            Context.AsyncActivityHandlers[WorkflowActivityNames.AgentTaskActivity] = HandleAgentTask;
            Context.ActivityHandlers[WorkflowActivityNames.SnapshotStateActivity] = input =>
            {
                var snapshot = (ExecutionSnapshot)input!;
                Snapshots.Add(snapshot);
                return new SnapshotActivityResponse { SnapshotId = $"{snapshot.ExecutionId}:{snapshot.Sequence:D6}" };
            };
            Context.ActivityHandlers[WorkflowActivityNames.ArtifactStateActivity] = input =>
            {
                var request = (ArtifactStateActivityRequest)input!;
                return new ArtifactStateActivityResponse { SectionRef = $"{request.ExecutionId}:{request.ArtifactKey}:v{request.Version}" };
            };
            Context.ActivityHandlers[WorkflowActivityNames.RequestApprovalActivity] = input =>
            {
                var request = (GateOpenRequest)input!;
                GateOpenings.Add(request);
                return ApprovalRequestFactory.Open(request);
            };
        }

        public WorkflowDefinition Definition { get; }
        public FakeTaskOrchestrationContext Context { get; }
        public List<string> StartedNodes { get; } = [];
        public List<ExecutionSnapshot> Snapshots { get; } = [];
        public List<GateOpenRequest> GateOpenings { get; } = [];

        public Task<GraphExecutionState> StartWalk() =>
            GraphOrchestratorSteps.RunInitialWalkAsync(Context, OrchestrationFixtures.Input(Definition), new RollbackPlanner(), PolicyProvider, OrchestrationFixtures.WriteClassifier);

        public void CompleteNode(string nodeId) => _pending[nodeId].SetResult(RunRealActivity(_inputs[nodeId]));

        public void FailNode(string nodeId, Exception exception) => _pending[nodeId].SetException(exception);

        private Task<object> HandleAgentTask(object? input)
        {
            var activityInput = (AgentTaskActivityInput)input!;
            StartedNodes.Add(activityInput.NodeId);
            _inputs[activityInput.NodeId] = activityInput;

            if (!_deferred.Contains(activityInput.NodeId))
            {
                return Task.FromResult<object>(RunRealActivity(activityInput));
            }

            var pending = new TaskCompletionSource<object>();
            _pending[activityInput.NodeId] = pending;
            return pending.Task;
        }

        private static AgentTaskActivityResult RunRealActivity(AgentTaskActivityInput activityInput) =>
            new AgentTaskActivity(new FakeAgentTaskActivityPipeline()).RunAsync(new FakeTaskActivityContext(), activityInput).GetAwaiter().GetResult();
    }
}
