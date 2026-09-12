using Frontier.Platform.Abstractions;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Hitl;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.62 — ADR-CR1's refresh loop inside the walk: the orchestrator receives a
/// <c>DynamicContextRefreshRequired</c> signal, decides what to do with it, and moves the run's
/// dynamic-context pin.
/// <para>
/// <b>What is bound here.</b> The event name (doc 04 §8's prose, doc 16 §4's refresh-class list
/// and doc 18 §3 all say <c>Required</c>; only the §8 code block says <c>Needed</c>, and it
/// contradicts the payload type it constructs on the next line). A refresh runs in an activity,
/// never in the body — the decision is the only part that is the orchestrator's, per hard
/// invariant 2 and the dtf-determinism skill. The pin lives in
/// <see cref="GraphExecutionState.DynamicContextEpoch"/>, the settable field S13.60 added and
/// documented as "moved only by … S13.62"; <c>state.UpdateDynamic(dynamic)</c>, which the plan
/// entry names, does not exist and never did.
/// </para>
/// <para>
/// <b>Refresh at quiescence.</b> The orchestrator refreshes when no nodes are in flight;
/// otherwise the signal is handled at the next iteration — doc 04 §8's "queue for next
/// checkpoint". Refreshing mid-flight would mean the run's pin moved while activities scheduled
/// against the old one were still running, so the snapshot would attest to an epoch that half the
/// completed steps never read.
/// </para>
/// <para>
/// <b>Two behaviours that look like bugs and are the design.</b> Nodes already in
/// <c>walk.Running</c> keep the epoch they were scheduled with — they pinned at schedule time,
/// which is the entire point of <see cref="AgentTaskActivityInput.DynamicContextEpoch"/>. And a
/// gate runs <em>inline</em> in the walk (<c>GraphOrchestratorSteps:110</c> → <c>RunGateAsync</c>),
/// so the walk is not inside <c>Task.WhenAny</c> while a gate is open; DTF buffers the signal
/// until the gate returns. Both are pinned below so nobody "fixes" them.
/// </para>
/// <para>
/// <b>The trap.</b> The refresh subscription is created <em>once, outside</em> the walk loop and
/// kept out of <c>walk.Running</c>, re-armed only after a signal is consumed. Creating it per
/// iteration writes a new subscription into history on every pass and consumes buffered events
/// out of order. Per the <c>GraphOrchestratorSteps:411–422</c> comment, do not hand-roll
/// <c>Task.WhenAny</c> + <c>CreateTimer</c> + <c>CancellationTokenSource.Cancel</c>: that races a
/// <c>TimerFired</c> history replay against an already-cancelled timer and throws.
/// </para>
/// </summary>
public sealed class GraphRefreshSignalWalkTests
{
    private static readonly FakeResiliencePolicyProvider PolicyProvider = new();

    /// <summary>The epoch the refresh activity reports back — deliberately far from the seeded pin, so a moved pin is unmistakable.</summary>
    private const int RefreshedEpoch = 7;

    private const int SeedEpoch = 0;
    private const string SeedHash = "seed-hash";
    private const string RefreshedHash = "refreshed-hash";

    /// <summary>
    /// The event name is a contract shared with the ingest surface that raises it. Pinned as a
    /// constant rather than a literal scattered through the walk, and pinned to the spelling the
    /// three consistent design sources agree on.
    /// </summary>
    [Fact]
    public void DynamicContextRefreshEventName_IsTheAdrCr1ContractName() =>
        Assert.Equal("DynamicContextRefreshRequired", GraphOrchestratorSteps.DynamicContextRefreshEventName);

    /// <summary>
    /// <b>The dangling-wait trap, and the case most likely to catch a wrong implementation.</b>
    /// Almost no run ever receives a refresh signal. If the walk awaits the refresh subscription
    /// in a way that must complete — an unguarded <c>await</c>, or a <c>WhenAll</c>, or a
    /// subscription parked in <c>walk.Running</c> where the loop counts it as outstanding work —
    /// then every ordinary execution hangs forever at the point the graph is otherwise finished.
    /// The subscription is allowed to be left dangling when the walk ends; DTF discards it with
    /// the instance.
    /// </summary>
    [Fact]
    public async Task NoSignalEverRaised_WalkStillTerminates()
    {
        var harness = new RefreshHarness(OrchestrationFixtures.FanOutJoin(), deferred: [], raiseSignal: false);

        var state = await harness.RunToCompletion();

        Assert.Equal(["a-entry", "b-booking", "b-ticket", "c-join"], state.CompletedSteps.Select(step => step.NodeId));
        Assert.Equal(SeedEpoch, state.DynamicContextEpoch);
        Assert.Equal(SeedHash, state.DynamicContextHash);
        Assert.Equal(0, harness.RefreshCalls);
    }

    /// <summary>
    /// The core of ADR-CR1. A signal arrives while both branch nodes are in flight: those two
    /// keep the epoch they were scheduled with, and the <em>next</em> node scheduled after the
    /// walk quiesces carries the refreshed one.
    /// </summary>
    [Fact]
    public async Task SignalWhileNodesInFlight_InFlightKeepTheirEpoch_NextScheduledCarriesTheNew()
    {
        var harness = new RefreshHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["b-booking", "b-ticket"], raiseSignal: false);
        var walk = harness.StartWalk();

        // Both branches are in flight, each pinned to the epoch it was scheduled with.
        Assert.Equal(["a-entry", "b-booking", "b-ticket"], harness.StartedNodes);
        harness.RaiseRefreshSignal();

        harness.CompleteNode("b-booking");
        harness.CompleteNode("b-ticket");
        var state = await WithTimeout(walk);

        Assert.Equal(SeedEpoch, harness.EpochFor("a-entry"));
        Assert.Equal(SeedEpoch, harness.EpochFor("b-booking"));
        Assert.Equal(SeedEpoch, harness.EpochFor("b-ticket"));
        Assert.Equal(RefreshedEpoch, harness.EpochFor("c-join"));
        Assert.Equal(RefreshedEpoch, state.DynamicContextEpoch);
        Assert.Equal(RefreshedHash, state.DynamicContextHash);
    }

    /// <summary>
    /// One signal, one refresh. A subscription re-armed carelessly — re-created inside the loop,
    /// or re-awaited without consuming — re-reads the same buffered event on every iteration and
    /// calls the refresh activity once per pass, writing a new epoch each time and invalidating
    /// the provider cache on every node. The delivery count is asserted alongside the call count
    /// so a correct re-arm (blocking again on a fresh subscription) is distinguished from a
    /// re-read.
    /// </summary>
    [Fact]
    public async Task SignalRaisedOnce_CallsTheRefreshActivityExactlyOnce()
    {
        var harness = new RefreshHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["b-booking", "b-ticket"], raiseSignal: false);
        var walk = harness.StartWalk();
        harness.RaiseRefreshSignal();

        harness.CompleteNode("b-booking");
        harness.CompleteNode("b-ticket");
        await WithTimeout(walk);

        Assert.Equal(1, harness.RefreshCalls);
        Assert.Equal(1, harness.SignalDeliveries);
    }

    /// <summary>
    /// The refresh request names the engagement the run belongs to and the reason the signal
    /// carried — ADR-CR1's "explicit reason", which <c>DynamicContextRefresher</c> already
    /// requires and tags its OTEL counter with. A refresh that reached the activity with no
    /// reason would be an unattributable cache invalidation.
    /// </summary>
    [Fact]
    public async Task RefreshActivity_ReceivesTheEngagementAndTheSignalsReason()
    {
        var harness = new RefreshHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["b-booking", "b-ticket"], raiseSignal: false);
        var walk = harness.StartWalk();
        harness.RaiseRefreshSignal();
        harness.CompleteNode("b-booking");
        harness.CompleteNode("b-ticket");
        await WithTimeout(walk);

        var request = Assert.Single(harness.RefreshRequests);
        Assert.Contains("eng-1", request, StringComparison.Ordinal);
        Assert.Contains(RefreshHarness.SignalReason, request, StringComparison.Ordinal);
    }

    /// <summary>
    /// A signal with no good refresh point: it arrives while the final node is in flight, so the
    /// walk never quiesces again with work left to schedule. The execution must still complete,
    /// the walk must be unaffected, and — the part that matters — no node may be scheduled on a
    /// pin the run never actually resolved. "Acknowledge and continue" is one of the three
    /// outcomes doc 04 §8 gives the sovereign orchestrator.
    /// </summary>
    [Fact]
    public async Task SignalWithNoGoodRefreshPoint_LeavesTheWalkAndEveryScheduledEpochUnchanged()
    {
        var harness = new RefreshHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["c-join"], raiseSignal: false);
        var walk = harness.StartWalk();

        // Everything upstream has finished; only the join is outstanding, so there is no later
        // scheduling point for a refreshed pin to reach.
        Assert.Equal(["a-entry", "b-booking", "b-ticket", "c-join"], harness.StartedNodes);
        harness.RaiseRefreshSignal();
        harness.CompleteNode("c-join");

        var state = await WithTimeout(walk);

        Assert.Equal(["a-entry", "b-booking", "b-ticket", "c-join"], state.CompletedSteps.Select(step => step.NodeId));
        Assert.All(harness.StartedNodes, nodeId => Assert.Equal(SeedEpoch, harness.EpochFor(nodeId)));
    }

    /// <summary>
    /// <b>Correct behaviour that will look like a bug.</b> A gate runs inline in the walk, so the
    /// orchestration is blocked inside <c>RunGateAsync</c>'s own
    /// <c>WaitForExternalEvent</c> — not inside the <c>Task.WhenAny</c> that watches the refresh
    /// subscription. A signal raised during the gate is therefore not observed until the gate
    /// returns. DTF buffers it; nothing is lost; doc 18 §3 states the same outcome from the other
    /// side ("instances idle at a gate don't refresh until they next assemble context — correct by
    /// design, no wasted work"). This test exists so nobody restructures the gate to race the
    /// refresh, which would mean opening a gate and a refresh against the same walk state.
    /// </summary>
    [Fact]
    public async Task SignalRaisedWhileGateIsOpen_IsBufferedNotLost_AndExecutionCompletes()
    {
        var harness = new RefreshHarness(OrchestrationFixtures.ChainWithBusinessGate(), deferred: [], raiseSignal: true);

        var state = await harness.RunToCompletion();

        Assert.Equal("gate-business-1", Assert.Single(harness.GateOpenings).GateId);
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["scope"]);
        // The signal was consumed exactly once across the run — buffered through the gate, not
        // dropped by it and not re-read after it.
        Assert.Equal(1, harness.RefreshCalls);
    }

    /// <summary>
    /// <b>Replay determinism over a run containing a refresh</b> (non-negotiable per the
    /// dtf-determinism skill). Identical history in must produce an identical schedule out: the
    /// same nodes in the same order, each carrying the same pinned epoch, and the same
    /// checkpoint sequence. A refresh that depended on wall-clock time, iteration count, or the
    /// arrival order of an unordered collection would diverge here — and a divergent replay
    /// corrupts an execution quietly and later, which is exactly why this is asserted rather than
    /// reasoned about.
    /// </summary>
    [Fact]
    public async Task ReplayOfARunContainingARefresh_SchedulesIdentically()
    {
        var first = await RunScriptedRefresh();
        var second = await RunScriptedRefresh();

        Assert.Equal(first.StartedNodes, second.StartedNodes);
        Assert.Equal(
            first.StartedNodes.Select(first.EpochFor),
            second.StartedNodes.Select(second.EpochFor));
        Assert.Equal(
            first.Snapshots.Select(snapshot => (snapshot.Sequence, snapshot.CurrentNodeId, snapshot.Status.Name, snapshot.DynamicContextEpoch)),
            second.Snapshots.Select(snapshot => (snapshot.Sequence, snapshot.CurrentNodeId, snapshot.Status.Name, snapshot.DynamicContextEpoch)));
        Assert.Equal(first.RefreshCalls, second.RefreshCalls);
    }

    /// <summary>
    /// The checkpoint written after a refresh cites the epoch the run moved to. The snapshot is
    /// the read projection the audit record is consolidated from (S13.60/S13.65), so a pin that
    /// moved in memory but not onto the evidence would reintroduce the linkage gap C-42 was
    /// opened for.
    /// </summary>
    [Fact]
    public async Task AfterARefresh_LaterCheckpointsCiteTheRefreshedEpoch()
    {
        var harness = await RunScriptedRefresh();

        Assert.Contains(harness.Snapshots, snapshot => snapshot.DynamicContextEpoch == SeedEpoch);
        Assert.Equal(RefreshedEpoch, harness.Snapshots[^1].DynamicContextEpoch);
        Assert.Equal(RefreshedHash, harness.Snapshots[^1].DynamicContextHash);
    }

    /// <summary>One fixed script: signal raised with both branches in flight, then both released in a fixed order.</summary>
    private static async Task<RefreshHarness> RunScriptedRefresh()
    {
        var harness = new RefreshHarness(OrchestrationFixtures.FanOutJoin(), deferred: ["b-booking", "b-ticket"], raiseSignal: false);
        var walk = harness.StartWalk();
        harness.RaiseRefreshSignal();
        harness.CompleteNode("b-booking");
        harness.CompleteNode("b-ticket");
        await WithTimeout(walk);
        return harness;
    }

    /// <summary>
    /// Fails the test rather than hanging it. A dangling-wait defect manifests as a walk that
    /// never completes, and an xunit run that hangs reports nothing useful.
    /// </summary>
    private static async Task<GraphExecutionState> WithTimeout(Task<GraphExecutionState> walk)
    {
        var completed = await Task.WhenAny(walk, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(ReferenceEquals(completed, walk), "the walk did not terminate — the refresh subscription is being awaited as if it must complete");
        return await walk;
    }

    /// <summary>
    /// Drives one walk with a refresh signal and per-node deferrable agent activities, recording
    /// the dynamic-context epoch every node was scheduled with. Modelled on
    /// <c>GraphConcurrentWalkTests.WalkHarness</c>.
    /// </summary>
    private sealed class RefreshHarness
    {
        /// <summary>ADR-CR1's worked example of an explicit refresh reason (doc 04 §8).</summary>
        public const string SignalReason = "CRM_pricing_changed";

        private readonly HashSet<string> deferred;
        private readonly Dictionary<string, TaskCompletionSource<object>> pending = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AgentTaskActivityInput> inputs = new(StringComparer.Ordinal);
        private readonly PendingExternalEvent signal = new();

        public RefreshHarness(WorkflowDefinition definition, IReadOnlyList<string> deferred, bool raiseSignal)
        {
            Definition = definition;
            this.deferred = new HashSet<string>(deferred, StringComparer.Ordinal);
            Context = new FakeTaskOrchestrationContext();
            Context.ExternalEvents[GraphOrchestratorSteps.DynamicContextRefreshEventName] = signal;
            Context.AsyncActivityHandlers[WorkflowActivityNames.AgentTaskActivity] = HandleAgentTask;
            Context.ActivityHandlers[WorkflowActivityNames.RefreshDynamicContextActivity] = input =>
            {
                RefreshCalls++;
                RefreshRequests.Add(System.Text.Json.JsonSerializer.Serialize(input, Frontier.Platform.Serialization.CanonicalProfile.Options));
                return new DynamicContextRefreshResult(Refreshed: true, Epoch: RefreshedEpoch, ContentHash: RefreshedHash);
            };
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
            foreach (var gate in definition.Nodes.OfType<HumanGateNode>())
            {
                Context.ExternalEvents[GraphOrchestratorSteps.GateEventName(gate.NodeId)] = Approval(gate.NodeId);
            }

            if (raiseSignal)
            {
                RaiseRefreshSignal();
            }
        }

        public WorkflowDefinition Definition { get; }
        public FakeTaskOrchestrationContext Context { get; }
        public List<string> StartedNodes { get; } = [];
        public List<ExecutionSnapshot> Snapshots { get; } = [];
        public List<GateOpenRequest> GateOpenings { get; } = [];
        public List<string> RefreshRequests { get; } = [];
        public int RefreshCalls { get; private set; }

        /// <summary>How many times a refresh wait was actually satisfied — a re-read shows up here as well as in <see cref="RefreshCalls"/>.</summary>
        public int SignalDeliveries => signal.DeliveredCount;

        /// <summary>The run's pin as the Host resolved it before scheduling (S13.60): the orchestrator body reads no store.</summary>
        public GraphOrchestratorInput Input => new()
        {
            Definition = Definition,
            EngagementId = "eng-1",
            DynamicContextEpoch = SeedEpoch,
            DynamicContextHash = SeedHash,
        };

        public Task<GraphExecutionState> StartWalk() =>
            GraphOrchestratorSteps.RunInitialWalkAsync(Context, Input, new RollbackPlanner(), PolicyProvider, OrchestrationFixtures.WriteClassifier);

        public Task<GraphExecutionState> RunToCompletion() => WithTimeout(StartWalk());

        /// <summary>
        /// Raises the single wire contract for this event (<see cref="DynamicContextRefreshRequired"/>,
        /// platform kernel) exactly as the consumer's ingest surface emits it — including the
        /// snake_case component names the refresh will merge, which are the document keys, not
        /// doc 18 §1's kebab-case tier-table spelling.
        /// </summary>
        public void RaiseRefreshSignal() => signal.Raise(new DynamicContextRefreshRequired
        {
            EngagementId = "eng-1",
            Reason = SignalReason,
            ChangedFields = ["commercial_terms", "client_budget"],
            DetectedAtUtc = OrchestrationFixtures.StartedAtUtc,
            Components = ["engagement_profile", "client_profile"],
        });

        /// <summary>The dynamic-context epoch <paramref name="nodeId"/> was scheduled with.</summary>
        public int? EpochFor(string nodeId) => inputs[nodeId].DynamicContextEpoch;

        public void CompleteNode(string nodeId) => pending[nodeId].SetResult(RunRealActivity(inputs[nodeId]));

        private Task<object> HandleAgentTask(object? input)
        {
            var activityInput = (AgentTaskActivityInput)input!;
            StartedNodes.Add(activityInput.NodeId);
            inputs[activityInput.NodeId] = activityInput;

            if (!deferred.Contains(activityInput.NodeId))
            {
                return Task.FromResult<object>(RunRealActivity(activityInput));
            }

            var waiter = new TaskCompletionSource<object>();
            pending[activityInput.NodeId] = waiter;
            return waiter.Task;
        }

        private static AgentTaskActivityResult RunRealActivity(AgentTaskActivityInput activityInput) =>
            new AgentTaskActivity(new FakeAgentTaskActivityPipeline()).RunAsync(new FakeTaskActivityContext(), activityInput).GetAwaiter().GetResult();

        private static HitlDecision Approval(string gateId) => new()
        {
            GateId = gateId,
            RequestId = $"eng-1::wf-chain-gate:{gateId}:0",
            ApproverId = "user:approver-1",
            Kind = DecisionKind.Approve,
            DecidedAtUtc = OrchestrationFixtures.StartedAtUtc,
        };
    }
}
