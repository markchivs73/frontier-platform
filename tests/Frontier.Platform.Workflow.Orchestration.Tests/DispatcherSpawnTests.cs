using Frontier.Platform.Abstractions;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.22 — the dispatcher as ADR-E8 actually specifies it: a router that spawns and keeps
/// listening, not a queue that runs one item at a time.
/// <para>
/// <b>The defect these bind out.</b> <c>DispatcherOrchestrator.cs:53-55</c> awaited
/// <c>CallSubOrchestratorAsync</c> <em>inside</em> the loop, before looping back to the
/// <c>WorkItem</c> wait at <c>:41</c>. One child parked at a human gate therefore blocked every
/// later work item — the exact inverse of doc 00 §4.4's "work items process in parallel: a ticket
/// paused at a human gate never blocks the queue", and of doc 16 §4's "children run normal
/// run-to-completion semantics … so work items process in parallel". Since a dispatcher-mode
/// definition typically contains a gate, the serial form would have parked the queue on the first
/// ticket and looked like nothing more than a slow system.
/// </para>
/// <para>
/// <b>Two behaviours below will read as bugs, and are the design</b> (owner's call, S13.22).
/// First, spawning is <b>unbounded</b>: the body races the <c>WorkItem</c> wait against every
/// outstanding child and never throttles. Second, <c>ContinueAsNew</c> counts <b>spawns, not
/// completions</b>, and fires <b>without awaiting the children</b> — DTF sub-orchestrations are
/// independent instances that survive their parent's generation change, so a rollover mid-flight
/// orphans nothing. Both are pinned here so neither is "fixed" into a queue.
/// </para>
/// </summary>
public sealed class DispatcherSpawnTests
{
    /// <summary>
    /// <b>The headline.</b> Two work items; child 1 never completes. If child 2 is still spawned,
    /// the dispatcher is a router; if it is not, the dispatcher is a serial queue and ADR-E8's
    /// parallelism rationale is unimplemented. One assertion, the whole point of the mode.
    /// </summary>
    [Fact]
    public void ChildThatNeverCompletes_DoesNotBlockTheNextWorkItem()
    {
        var harness = new DispatcherHarness();
        var run = harness.Start();

        harness.RaiseWorkItem("TICKET-1");
        harness.RaiseWorkItem("TICKET-2");

        Assert.Equal(["TICKET-1", "TICKET-2"], harness.ChildWorkItemIds);
        Assert.False(run.IsCompleted, "the dispatcher returned while children were still outstanding");
    }

    /// <summary>
    /// The router keeps routing indefinitely while every child it has ever spawned is still parked.
    /// A throttle, a bounded channel, or a <c>WhenAll</c> anywhere in the loop fails here.
    /// </summary>
    [Fact]
    public void ManyWorkItems_NoneOfTheirChildrenCompleting_AllStillSpawn()
    {
        var harness = new DispatcherHarness();
        harness.Start();

        harness.RaiseWorkItems(12);

        Assert.Equal(12, harness.ChildInputs.Count);
        Assert.Equal(0, harness.CompletedChildren);
    }

    /// <summary>
    /// <b>History shape.</b> The <c>WorkItem</c> subscription is created once and re-armed only
    /// after a delivery — never re-created per pass of the loop. Once the body races that wait
    /// against outstanding children, a wait rebuilt each iteration writes a new subscription into
    /// DTF history on every pass and consumes buffered events out of order; that is the hazard
    /// <c>GraphOrchestratorSteps.cs:70-77</c> documents for the refresh wait, and the dispatcher
    /// inherits it wholesale. Deliveries + 1 is exactly "one outstanding subscription, always".
    /// </summary>
    [Fact]
    public void WorkItemWait_IsSubscribedOncePerDelivery_PlusOneOutstanding()
    {
        var harness = new DispatcherHarness();
        harness.Start();

        harness.RaiseWorkItems(3);

        Assert.Equal(4, harness.Subscriptions(DispatcherOrchestrator.WorkItemEventName));
    }

    /// <summary>
    /// The generation boundary is counted in <b>spawns</b>. It fires on the Nth work item with all
    /// N children still outstanding — the dispatcher does not wait for them, because they are
    /// independent DTF instances that outlive the parent's generation.
    /// </summary>
    [Fact]
    public async Task ContinueAsNew_FiresOnTheNthSpawn_WithEveryChildStillOutstanding()
    {
        var harness = new DispatcherHarness();
        var run = harness.Start();

        harness.RaiseWorkItems(DispatcherOrchestrator.ContinueAsNewThreshold);

        Assert.Equal(DispatcherOrchestrator.ContinueAsNewThreshold, harness.ChildInputs.Count);
        Assert.Equal(0, harness.CompletedChildren);
        Assert.Single(harness.Context.ContinueAsNewCalls);
        await harness.WithTimeout(run);
    }

    /// <summary>
    /// Buffered work items must cross the boundary, or an item raised in the instant between the
    /// Nth spawn and the new generation is silently lost — an ingest surface that accepted the
    /// event and dropped it.
    /// </summary>
    [Fact]
    public void ContinueAsNew_PreservesUnprocessedEvents()
    {
        var harness = new DispatcherHarness();
        harness.Start();

        harness.RaiseWorkItems(DispatcherOrchestrator.ContinueAsNewThreshold);

        Assert.True(harness.Context.ContinueAsNewCalls[0].PreserveUnprocessedEvents);
    }

    /// <summary>
    /// The next generation's input is <b>rebuilt</b>, not the original handed back. It carries the
    /// freshly resolved definition (S13.18) while preserving the run's identity and attribution:
    /// a generation change is the same run continuing, so a new <c>RunId</c> here would fork the
    /// audit chain (ADR-EX1) and a dropped <c>InitiatedBy</c> would break the S13.19 attribution
    /// chain at an arbitrary 100-item boundary.
    /// </summary>
    [Fact]
    public void ContinueAsNew_RebuildsItsInput_PreservingRunIdEngagementAndInitiator()
    {
        var rolled = OrchestrationFixtures.ThreeArtifactChain(ExecutionMode.Dispatcher) with { DefinitionVersion = 9 };
        var harness = new DispatcherHarness(rollover: rolled);
        harness.Start();

        harness.RaiseWorkItems(DispatcherOrchestrator.ContinueAsNewThreshold);

        var next = Assert.IsType<GraphOrchestratorInput>(harness.Context.ContinueAsNewCalls[0].Input);
        Assert.Equal(9, next.Definition.DefinitionVersion);
        Assert.Equal(DispatcherFixtures.DispatcherRunId, next.RunId);
        Assert.Equal("E2E::Acme::HQ", next.EngagementId);
        Assert.Equal("user:oid-dispatcher-starter", next.InitiatedBy);
    }

    /// <summary>
    /// <b>Child identity comes from <c>context.NewGuid()</c></b> (S13.22 Q1) — DTF's recorded,
    /// replay-stable GUID, the one source hard invariant 2 permits a body. Each child gets its own
    /// run id: children are independent executions with their own snapshots and their own signed
    /// audit records, so sharing the parent's run id would collapse them into one run's evidence.
    /// </summary>
    [Fact]
    public void EachChild_GetsItsOwnRunIdFromTheContext()
    {
        var harness = new DispatcherHarness();
        harness.Start();

        harness.RaiseWorkItems(3);

        var runIds = harness.ChildInputs.Select(child => child.RunId).ToList();
        Assert.All(runIds, runId => Assert.False(string.IsNullOrWhiteSpace(runId)));
        Assert.Equal(3, runIds.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(DispatcherFixtures.DispatcherRunId, runIds);
        Assert.Equal(3, harness.Context.NewGuidCallCount);
    }

    /// <summary>
    /// The child inherits the dispatcher's dynamic-context pin. Without it a child resolves
    /// "current" at its own start, so two tickets dispatched from one generation could run against
    /// different context epochs while their snapshots claim the same provenance (S13.60/C-42).
    /// </summary>
    [Fact]
    public void EachChild_CarriesTheDispatchersDynamicContextPin()
    {
        var harness = new DispatcherHarness();
        harness.Start();

        harness.RaiseWorkItem("TICKET-1");

        var child = harness.ChildInputs[0];
        Assert.Equal(DispatcherFixtures.Epoch, child.DynamicContextEpoch);
        Assert.Equal(DispatcherFixtures.EpochHash, child.DynamicContextHash);
    }

    /// <summary>
    /// <b>Replay determinism</b> (non-negotiable, dtf-determinism skill). One work item buffered
    /// before the instance resumes and one child already in flight is the shape a replay actually
    /// meets. Identical history in must produce an identical child set out — same items, same
    /// order, same run ids, and above all no duplicate spawn, since a re-spawned child is a second
    /// execution of the same ticket with its own audit record and its own side effects.
    /// </summary>
    [Fact]
    public void ReplayOfARunWithAChildInFlightAndAnItemBuffered_SpawnsIdentically()
    {
        var first = RunScriptedDispatch();
        var second = RunScriptedDispatch();

        Assert.Equal(first.ChildWorkItemIds, second.ChildWorkItemIds);
        Assert.Equal(
            first.ChildInputs.Select(child => child.RunId),
            second.ChildInputs.Select(child => child.RunId));
        Assert.Equal(2, first.ChildInputs.Count);
    }

    /// <summary>One fixed script: an item buffered before the body subscribes, then one delivered live while the first child is still in flight.</summary>
    private static DispatcherHarness RunScriptedDispatch()
    {
        var harness = new DispatcherHarness();
        harness.RaiseWorkItem("TICKET-BUFFERED");
        harness.Start();
        harness.RaiseWorkItem("TICKET-LIVE");
        return harness;
    }

    /// <summary>
    /// <b>The dispatcher drains refresh signals it will never act on</b> (S13.22, owner's call).
    /// S13.62's fan-out raises <c>DynamicContextRefreshRequired</c> at every live instance of an
    /// engagement, dispatchers included. The dispatcher has no walk and no epoch to move, so it
    /// never acts on one — but <c>ContinueAsNew(preserveUnprocessedEvents: true)</c> would then
    /// carry every such signal into the next generation, and the next, forever: an unbounded
    /// buffer that grows for the life of an eternal instance. A drain-and-discard wait bounds it.
    /// </summary>
    [Fact]
    public void RefreshSignalRaisedAtADispatcher_IsDrainedAndDiscarded()
    {
        var harness = new DispatcherHarness();
        harness.Start();

        harness.RaiseRefreshSignal();

        Assert.Equal(1, harness.RefreshDeliveries);
        Assert.Equal(2, harness.Subscriptions(GraphOrchestratorSteps.DynamicContextRefreshEventName));
        Assert.Equal(0, harness.RefreshActivityCalls);
    }

    /// <summary>Draining is not consuming: the router still routes work after a refresh signal passes through it.</summary>
    [Fact]
    public void AfterDrainingARefreshSignal_WorkItemsStillSpawnChildren()
    {
        var harness = new DispatcherHarness();
        harness.Start();

        harness.RaiseRefreshSignal();
        harness.RaiseWorkItem("TICKET-1");

        Assert.Equal(["TICKET-1"], harness.ChildWorkItemIds);
    }

    /// <summary>Nothing buffered at the boundary: signals raised during a generation are gone by the time it rolls over.</summary>
    [Fact]
    public void RefreshSignalsRaisedDuringAGeneration_DoNotSurviveIntoTheNext()
    {
        var harness = new DispatcherHarness();
        harness.Start();

        harness.RaiseRefreshSignal();
        harness.RaiseRefreshSignal();
        harness.RaiseWorkItems(DispatcherOrchestrator.ContinueAsNewThreshold);

        Assert.Equal(2, harness.RefreshDeliveries);
        Assert.Single(harness.Context.ContinueAsNewCalls);
    }

    /// <summary>
    /// Drives one dispatcher generation: work items and refresh signals raised at chosen moments,
    /// every spawned child held open until the test releases it. Modelled on
    /// <c>GraphRefreshSignalWalkTests.RefreshHarness</c>.
    /// </summary>
    internal sealed class DispatcherHarness
    {
        private readonly PendingExternalEvent workItems = new();
        private readonly PendingExternalEvent refreshes = new();
        private readonly Dictionary<int, TaskCompletionSource<object>> children = [];
        private readonly WorkflowDefinition? rollover;

        internal DispatcherHarness(WorkflowDefinition? rollover = null)
        {
            this.rollover = rollover;
            Context = new FakeTaskOrchestrationContext();
            Context.ExternalEvents[DispatcherOrchestrator.WorkItemEventName] = workItems;
            Context.ExternalEvents[GraphOrchestratorSteps.DynamicContextRefreshEventName] = refreshes;

            // Every child is held open. A test that wants one finished calls CompleteChild.
            Context.SubOrchestratorHandler = (ordinal, _) =>
            {
                var pending = new TaskCompletionSource<object>();
                children[ordinal] = pending;
                return pending.Task;
            };

            Context.WithVersionResolver(this.rollover, ResolveRequests);

            // The dispatcher must never call this. Registered so that calling it is a recorded
            // failure rather than an unhandled "no handler registered" exception.
            Context.ActivityHandlers[WorkflowActivityNames.RefreshDynamicContextActivity] = _ =>
            {
                RefreshActivityCalls++;
                return new DynamicContextRefreshResult(Refreshed: true, Epoch: 99, ContentHash: "unexpected");
            };
        }

        internal FakeTaskOrchestrationContext Context { get; }

        /// <summary>Requests captured from the S13.18 rollover activity.</summary>
        internal List<ResolveDispatcherVersionRequest> ResolveRequests { get; } = [];

        /// <summary>How many times the dispatcher called the refresh activity — must always be zero.</summary>
        internal int RefreshActivityCalls { get; private set; }

        /// <summary>How many refresh signals were actually delivered to a waiting body.</summary>
        internal int RefreshDeliveries => refreshes.DeliveredCount;

        internal IReadOnlyList<GraphOrchestratorInput> ChildInputs =>
            [.. Context.SubOrchestratorInputs.Cast<GraphOrchestratorInput>()];

        internal IReadOnlyList<string?> ChildWorkItemIds => [.. ChildInputs.Select(child => child.WorkItemId)];

        internal int CompletedChildren => children.Values.Count(child => child.Task.IsCompleted);

        internal int Subscriptions(string eventName) => Context.ExternalEventSubscriptions.GetValueOrDefault(eventName);

        internal Task<GraphOrchestratorResult> Start(GraphOrchestratorInput? input = null) =>
            new DispatcherOrchestrator().RunAsync(Context, input ?? DispatcherFixtures.Input());

        internal void RaiseWorkItem(string workItemId, string? directedBy = null) =>
            workItems.Raise(DispatcherFixtures.Item(workItemId, directedBy));

        internal void RaiseWorkItems(int count)
        {
            for (var i = 1; i <= count; i++)
            {
                RaiseWorkItem($"TICKET-{i:D3}");
            }
        }

        internal void RaiseRefreshSignal() => refreshes.Raise(new DynamicContextRefreshRequired
        {
            EngagementId = "E2E::Acme::HQ",
            Reason = "CRM_pricing_changed",
            ChangedFields = ["commercial_terms"],
            DetectedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        /// <summary>Releases one outstanding child.</summary>
        internal void CompleteChild(int ordinal) =>
            children[ordinal].SetResult(new GraphOrchestratorResult
            {
                CompletedSteps = [],
                ArtifactStatuses = new Dictionary<string, ArtifactStatus>(StringComparer.Ordinal),
            });

        /// <summary>Fails rather than hangs: a dispatcher that awaits its children never returns, and a hung xunit run reports nothing useful.</summary>
        internal async Task<GraphOrchestratorResult> WithTimeout(Task<GraphOrchestratorResult> run)
        {
            var completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(ReferenceEquals(completed, run), $"the dispatcher did not return at the generation boundary — it is awaiting its {children.Count} children");
            return await run;
        }
    }
}
