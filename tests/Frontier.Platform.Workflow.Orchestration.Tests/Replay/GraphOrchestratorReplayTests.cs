using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace Frontier.Platform.Workflow.Orchestration.Tests.Replay;

/// <summary>
/// S13.192 (review-01 ENG-04): <see cref="GraphOrchestrator"/> on a real Durable Task worker against
/// the DTS emulator, where history is recorded and replayed and activities complete on the worker's
/// own schedule. <see cref="FakeTaskOrchestrationContext"/> never replays and completes activities
/// inline, which is how both orchestrator P0s stayed invisible to the unit suite. The model is
/// scripted (<see cref="ScriptedAgentTaskActivityPipeline"/>), so this needs no key and costs nothing.
/// </summary>
[Trait("Category", "Integration")]
public sealed class GraphOrchestratorReplayTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>How long cleanup waits for a terminated run to finish before leaving it unpurged.</summary>
    private static readonly TimeSpan TerminationGrace = TimeSpan.FromSeconds(30);

    /// <summary>Every run this test started, so <see cref="DisposeAsync"/> can purge it whatever the outcome.</summary>
    private readonly List<string> _instanceIds = [];

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Purges every run this test started, terminating first any a failed test left live. A live run
    /// left in a shared hub would be dispatched to the next worker that attaches to it. The cleanup
    /// host is a full worker because a termination only takes effect when a worker processes it; it
    /// can also pick up that run's other pending work in the moment before it does.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var cleanup = await ReplayWorkerHost.StartAsync("cleanup", new ReplayRecorder());
        foreach (var instanceId in _instanceIds)
        {
            await RemoveAsync(cleanup.Client, instanceId);
        }
    }

    /// <summary>
    /// A run parks at its gate on worker A; A stops and a fresh worker B, with a new container and
    /// no memory of the run, takes the decision. B can only finish the walk by replaying A's history:
    /// the result still carries the three steps A ran, and no agent node is invoked a second time.
    /// </summary>
    [Fact]
    public async Task GatedRun_ResumesOnAFreshWorkerByReplay_WithoutReRunningAnyStep()
    {
        var recorder = new ReplayRecorder();
        var instanceId = NewInstanceId();

        await using (var first = await ReplayWorkerHost.StartAsync("worker-a", recorder))
        {
            await first.Client.ScheduleNewOrchestrationInstanceAsync(
                WorkflowActivityNames.GraphOrchestrator, Input(OrchestrationFixtures.ChainWithBusinessGate()), new StartOrchestrationOptions { InstanceId = instanceId });
            await WaitUntilAsync(() => recorder.Snapshots.Any(s => s.Snapshot.Status == ExecutionStatus.PausedAtGate), "the run to park at its gate");
            await WaitForCheckpointToLandAsync(first.Client, instanceId, recorder.Snapshots[^1].RecordedAt);
        }

        await using var second = await ReplayWorkerHost.StartAsync("worker-b", recorder);
        await second.Client.RaiseEventAsync(instanceId, GraphOrchestratorSteps.GateEventName("gate-business-1"), Approve(Assert.Single(recorder.Approvals)));
        var result = await CompleteAsync(second.Client, instanceId);

        Assert.Equal(["scope-agent", "approach-agent", "pricing-agent"], result.CompletedSteps.Select(step => step.NodeId));
        Assert.Equal(["scope-agent", "approach-agent", "pricing-agent"], recorder.AgentCalls.Select(call => call.NodeId));
        Assert.All(recorder.AgentCalls, call => Assert.Equal("worker-a", call.Worker));
        Assert.Equal(["worker-b"], recorder.AuditWorkers);
        AssertCheckpointsAreUniqueAndEndCompleted(recorder);
        Assert.Equal("worker-b", recorder.Snapshots[^1].Worker);
    }

    /// <summary>
    /// Review-01 ENG-02 under real interleaving. The two branches run at the same time on the worker,
    /// the declared-later branch finishes first, and the slower one completes while the first one's
    /// checkpoint write is still held open (its end falls inside that write's start-to-land window). Each checkpoint must keep its own sequence number.
    /// </summary>
    [Fact]
    public async Task FanOut_BranchesInterleaveOnTheWorker_AndEachCheckpointKeepsItsOwnSequence()
    {
        var recorder = new ReplayRecorder();
        var instanceId = NewInstanceId();
        var delays = new Dictionary<string, TimeSpan>
        {
            ["b-booking"] = TimeSpan.FromSeconds(2),
            ["b-ticket"] = TimeSpan.FromMilliseconds(500),
        };

        await using var worker = await ReplayWorkerHost.StartAsync("worker-a", recorder, delays, snapshotDelay: TimeSpan.FromSeconds(2));
        await worker.Client.ScheduleNewOrchestrationInstanceAsync(
            WorkflowActivityNames.GraphOrchestrator, Input(OrchestrationFixtures.FanOutJoin()), new StartOrchestrationOptions { InstanceId = instanceId });
        var result = await CompleteAsync(worker.Client, instanceId);

        var booking = recorder.AgentCalls.Single(call => call.NodeId == "b-booking");
        var ticket = recorder.AgentCalls.Single(call => call.NodeId == "b-ticket");
        Assert.True(booking.StartedAt < ticket.EndedAt && ticket.StartedAt < booking.EndedAt, "the branches never ran at the same time");
        Assert.Contains(recorder.Snapshots, s => IsTicketOnlyCheckpoint(s) && s.StartedAt < booking.EndedAt && booking.EndedAt < s.RecordedAt);
        Assert.Equal(["a-entry", "b-ticket", "b-booking", "c-join"], result.CompletedSteps.Select(step => step.NodeId));
        Assert.Equal(4, recorder.AgentCalls.Count);
        AssertCheckpointsAreUniqueAndEndCompleted(recorder);
    }

    /// <summary>
    /// Review-01 ENG-01 under real delivery. Two refresh signals arrive while both branches are in
    /// flight (raised before either branch ends). The walk must keep yielding so the branches'
    /// results land, then apply both refreshes, in arrival order, at quiescence: each refresh runs
    /// after both branches end and before the join starts. The join is scheduled after them, so it carries the second
    /// epoch; the branches keep the epoch they were scheduled with.
    /// </summary>
    [Fact]
    public async Task TwoRefreshSignalsWhileBranchesAreInFlight_BothApplyAtQuiescence_AndTheRunCompletes()
    {
        var recorder = new ReplayRecorder();
        var instanceId = NewInstanceId();
        var input = Input(OrchestrationFixtures.FanOutJoin());
        var delays = new Dictionary<string, TimeSpan> { ["b-booking"] = TimeSpan.FromSeconds(4), ["b-ticket"] = TimeSpan.FromSeconds(4) };

        await using var worker = await ReplayWorkerHost.StartAsync("worker-a", recorder, delays);
        await worker.Client.ScheduleNewOrchestrationInstanceAsync(WorkflowActivityNames.GraphOrchestrator, input, new StartOrchestrationOptions { InstanceId = instanceId });
        // Wait for both branches to start, not just the entry to finish: between the entry ending and
        // the branches being scheduled the walk is quiescent, and a refresh applied there is correct.
        await WaitUntilAsync(() => recorder.StartedNodes.Count(node => node.StartsWith("b-", StringComparison.Ordinal)) == 2, "both branches to start");
        await worker.Client.RaiseEventAsync(instanceId, GraphOrchestratorSteps.DynamicContextRefreshEventName, Signal(input.EngagementId, "first"));
        await worker.Client.RaiseEventAsync(instanceId, GraphOrchestratorSteps.DynamicContextRefreshEventName, Signal(input.EngagementId, "second"));
        var raisedAt = DateTimeOffset.UtcNow;
        await CompleteAsync(worker.Client, instanceId);

        var branchesEnded = recorder.AgentCalls.Where(call => call.NodeId.StartsWith("b-", StringComparison.Ordinal)).Select(call => call.EndedAt).ToArray();
        var join = recorder.AgentCalls.Single(call => call.NodeId == "c-join");
        Assert.True(raisedAt < branchesEnded.Min(), "a branch ended before both signals were raised");
        Assert.Equal(["first", "second"], recorder.Refreshes.Select(refresh => refresh.Reason));
        Assert.All(recorder.Refreshes, refresh => Assert.InRange(refresh.AppliedAt, branchesEnded.Max(), join.StartedAt));
        Assert.Null(recorder.AgentCalls.Single(call => call.NodeId == "b-booking").Epoch);
        Assert.Equal(2, recorder.AgentCalls.Single(call => call.NodeId == "c-join").Epoch);
    }

    /// <summary>
    /// S13.240 (ADR-PA46): the failure path under real replay. One branch fails while its sibling is still
    /// in flight, so the drain, the failed checkpoint and the audit consolidation each run in a
    /// later episode that replays the ones before. The run ends failed with the node's own failure, after
    /// exactly one consolidation attesting <c>failed</c>, and no agent node runs twice.
    /// </summary>
    [Fact]
    public async Task BranchFails_RunEndsFailedWithTheNodesFailure_AfterOneFailedAuditConsolidation()
    {
        var recorder = new ReplayRecorder();
        var instanceId = NewInstanceId();
        var delays = new Dictionary<string, TimeSpan> { ["b-ticket"] = TimeSpan.FromSeconds(3) };

        await using var worker = await ReplayWorkerHost.StartAsync("worker-a", recorder, delays, failingNodes: new HashSet<string>(["b-booking"], StringComparer.Ordinal));
        await worker.Client.ScheduleNewOrchestrationInstanceAsync(WorkflowActivityNames.GraphOrchestrator, Input(OrchestrationFixtures.FanOutJoin()), new StartOrchestrationOptions { InstanceId = instanceId });
        var metadata = await FinishAsync(worker.Client, instanceId);

        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.True(metadata.FailureDetails?.InnerFailure?.IsCausedBy<ContractViolationException>(), $"the run's failure is not the node's: {metadata.FailureDetails}");
        var audit = Assert.Single(recorder.Audits);
        Assert.Equal(ExecutionStatus.Failed, audit.Input.FinalStatus);
        var paused = recorder.Snapshots.MaxBy(s => s.Snapshot.Sequence)!;
        Assert.Equal(ExecutionStatus.Failed, paused.Snapshot.Status);
        Assert.Equal("b-booking", paused.Snapshot.CurrentNodeId);
        Assert.True(paused.RecordedAt <= audit.RecordedAt, "the audit was consolidated before the checkpoint it reads had landed");
        Assert.Equal(["a-entry", "b-ticket"], recorder.AgentCalls.Select(call => call.NodeId));
        Assert.Equal(["a-entry", "b-booking", "b-ticket"], recorder.StartedNodes.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// S13.247 under the real SDK: each section's first artifact write throws a passing fault. The
    /// storage profile's outer retry re-runs the write, so the run completes and every section was
    /// written exactly twice.
    /// </summary>
    [Fact]
    public async Task ArtifactWriteFaultsOnce_TheStorageProfileRetriesIt_AndTheRunCompletes()
    {
        var recorder = new ReplayRecorder();
        var instanceId = NewInstanceId();

        await using var worker = await ReplayWorkerHost.StartAsync("worker-a", recorder, artifactFault: new TimeoutException("store unavailable"));
        await worker.Client.ScheduleNewOrchestrationInstanceAsync(WorkflowActivityNames.GraphOrchestrator, Input(OrchestrationFixtures.ThreeArtifactChain()), new StartOrchestrationOptions { InstanceId = instanceId });
        await CompleteAsync(worker.Client, instanceId);

        Assert.Equal(["approach", "approach", "pricing", "pricing", "scope", "scope"], recorder.ArtifactWrites.Order(StringComparer.Ordinal));
        AssertCheckpointsAreUniqueAndEndCompleted(recorder);
    }

    /// <summary>S13.247 under the real SDK: a contract violation from the artifact write is permanent, so it runs once and the run fails with it.</summary>
    [Fact]
    public async Task ArtifactWriteContractViolation_IsNotRetried_AndTheRunFails()
    {
        var recorder = new ReplayRecorder();
        var instanceId = NewInstanceId();

        await using var worker = await ReplayWorkerHost.StartAsync("worker-a", recorder, artifactFault: new ContractViolationException("ArtifactStateActivityRequest", ["bad"]));
        await worker.Client.ScheduleNewOrchestrationInstanceAsync(WorkflowActivityNames.GraphOrchestrator, Input(OrchestrationFixtures.ThreeArtifactChain()), new StartOrchestrationOptions { InstanceId = instanceId });
        var metadata = await FinishAsync(worker.Client, instanceId);

        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.True(metadata.FailureDetails?.InnerFailure?.IsCausedBy<ContractViolationException>(), $"the run's failure is not the write's: {metadata.FailureDetails}");
        Assert.Equal(["scope"], recorder.ArtifactWrites);
    }

    /// <summary>One checkpoint per sequence number, numbered from 1 with no gap, the last one <see cref="ExecutionStatus.Completed"/>.</summary>
    private static void AssertCheckpointsAreUniqueAndEndCompleted(ReplayRecorder recorder)
    {
        var sequences = recorder.Snapshots.Select(s => s.Snapshot.Sequence).Order().ToArray();
        Assert.Equal(Enumerable.Range(1, sequences.Length), sequences);
        var last = recorder.Snapshots.MaxBy(s => s.Snapshot.Sequence)!;
        Assert.Equal(ExecutionStatus.Completed, last.Snapshot.Status);
    }

    /// <summary>The checkpoint written after <c>b-ticket</c> completed and before <c>b-booking</c> did.</summary>
    private static bool IsTicketOnlyCheckpoint(RecordedSnapshot snapshot)
    {
        var nodeIds = snapshot.Snapshot.CompletedSteps.Select(step => step.NodeId).ToHashSet(StringComparer.Ordinal);
        return nodeIds.Contains("b-ticket") && !nodeIds.Contains("b-booking");
    }

    /// <summary>Waits for <paramref name="instanceId"/> to finish, requires it completed, and returns its result.</summary>
    private static async Task<GraphOrchestratorResult> CompleteAsync(DurableTaskClient client, string instanceId)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var metadata = await client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, timeout.Token);
        Assert.True(metadata.RuntimeStatus == OrchestrationRuntimeStatus.Completed, $"{metadata.RuntimeStatus}: {metadata.FailureDetails}");
        return metadata.ReadOutputAs<GraphOrchestratorResult>()!;
    }

    /// <summary>Waits for <paramref name="instanceId"/> to finish, whatever its outcome, and returns its metadata with its failure details.</summary>
    private static async Task<OrchestrationMetadata> FinishAsync(DurableTaskClient client, string instanceId)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        return await client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, timeout.Token);
    }

    /// <summary>
    /// Purges <paramref name="instanceId"/>, terminating it first if it is still live. The wait for
    /// the termination is bounded by <see cref="TerminationGrace"/>: a walk that never yields
    /// (ENG-01's shape) never processes its termination, and an unbounded wait would hang cleanup
    /// behind the very defect the test just reported. Such a run is left in the hub, terminating
    /// but unpurged, since the scheduler purges only finished runs.
    /// </summary>
    private static async Task RemoveAsync(DurableTaskClient client, string instanceId)
    {
        var metadata = await client.GetInstanceAsync(instanceId);
        if (metadata is null)
        {
            return;
        }

        if (!metadata.IsCompleted && !await TerminateAsync(client, instanceId))
        {
            return;
        }

        await client.PurgeInstanceAsync(instanceId);
    }

    /// <summary>Terminates <paramref name="instanceId"/> and reports whether it finished within <see cref="TerminationGrace"/>.</summary>
    private static async Task<bool> TerminateAsync(DurableTaskClient client, string instanceId)
    {
        await client.TerminateInstanceAsync(instanceId);
        using var grace = new CancellationTokenSource(TerminationGrace);
        try
        {
            await client.WaitForInstanceCompletionAsync(instanceId, grace.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits until the scheduler has taken the episode that consumed the gate checkpoint, so stopping
    /// the worker cannot lose that activity's result. A lost result is redelivered, legitimately, as a
    /// second write of the same sequence, which would make the uniqueness assertion flaky.
    /// </summary>
    private static async Task WaitForCheckpointToLandAsync(DurableTaskClient client, string instanceId, DateTimeOffset recordedAt)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while ((await client.GetInstanceAsync(instanceId))?.LastUpdatedAt < recordedAt)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out after {Timeout} waiting for the gate checkpoint to land.");
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    /// <summary>Polls <paramref name="condition"/> until it holds, failing with what it was waiting for.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out after {Timeout} waiting for {what}.");
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    /// <summary>A run token unique to this test run, recorded for cleanup.</summary>
    private string NewInstanceId()
    {
        var instanceId = $"replay-{Guid.NewGuid():N}";
        _instanceIds.Add(instanceId);
        return instanceId;
    }

    private static GraphOrchestratorInput Input(WorkflowDefinition definition) =>
        OrchestrationFixtures.Input(definition, engagementId: $"eng-replay-{Guid.NewGuid():N}");

    /// <summary>A dynamic-context refresh signal for <paramref name="engagementId"/> carrying <paramref name="reason"/>.</summary>
    private static DynamicContextRefreshRequired Signal(string engagementId, string reason) => new()
    {
        EngagementId = engagementId,
        Reason = reason,
        ChangedFields = ["engagement.scope"],
        DetectedAtUtc = DateTime.UtcNow,
    };

    /// <summary>An approval of <paramref name="approval"/>, naming its request so the gate takes it as this visit's decision.</summary>
    private static HitlDecision Approve(Hitl.ApprovalRequest approval) => new()
    {
        GateId = approval.GateId,
        RequestId = approval.Id,
        ApproverId = "user:approver-1",
        Kind = DecisionKind.Approve,
        DecidedAtUtc = DateTime.UtcNow,
    };
}
