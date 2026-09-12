using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// ADR-PA26: a dispatcher child writes its own <b>sequence-0</b> snapshot before its walk begins,
/// and every snapshot it writes says which work item it is serving.
/// <para>
/// Sequence 0 is the pre-start projection slot the Host's <c>OrchestrationFactory</c> fills for a
/// normal run (S4.7a). A child bypasses that factory — it is spawned by
/// <see cref="DispatcherOrchestrator"/> — so before this nothing wrote its slot and the child was
/// invisible in the projection until its first node completed. A run that exists but cannot be
/// seen is the S13.70 failure mode, already fixed once for test runs.
/// </para>
/// </summary>
public sealed class DispatcherChildProjectionTests
{
    private readonly GraphOrchestrator orchestrator = new(new FakeResiliencePolicyProvider(), new RollbackPlanner(), OrchestrationFixtures.WriteClassifier);

    [Fact]
    public async Task RunAsync_DispatcherChild_WritesASequenceZeroSnapshotBeforeAnyNodeRuns()
    {
        var snapshots = new List<ExecutionSnapshot>();
        var context = Harness(snapshots);

        await orchestrator.RunAsync(context, ChildInput("TICKET-1"));

        Assert.Equal(0, snapshots[0].Sequence);
        Assert.Empty(snapshots[0].CompletedSteps);
        Assert.Equal(ExecutionStatus.Running, snapshots[0].Status);
    }

    [Fact]
    public async Task RunAsync_DispatcherChild_SequenceZeroSnapshotCarriesItsWorkItemAndMode()
    {
        var snapshots = new List<ExecutionSnapshot>();
        var context = Harness(snapshots);

        await orchestrator.RunAsync(context, ChildInput("TICKET-1"));

        Assert.Equal("TICKET-1", snapshots[0].WorkItemId);
        Assert.Equal(ExecutionMode.Dispatcher, snapshots[0].ExecutionMode);
    }

    /// <summary>Every checkpoint carries it, not just the first — the projection is read at any point in the run.</summary>
    [Fact]
    public async Task RunAsync_DispatcherChild_EveryLaterSnapshotAlsoCarriesTheWorkItem()
    {
        var snapshots = new List<ExecutionSnapshot>();
        var context = Harness(snapshots);

        await orchestrator.RunAsync(context, ChildInput("TICKET-1"));

        Assert.All(snapshots, snapshot => Assert.Equal("TICKET-1", snapshot.WorkItemId));
    }

    /// <summary>Sequence 0 is written once and the walk's own counter still starts at 1, so nothing overwrites it.</summary>
    [Fact]
    public async Task RunAsync_DispatcherChild_SequencesAreContiguousFromZero()
    {
        var snapshots = new List<ExecutionSnapshot>();
        var context = Harness(snapshots);

        await orchestrator.RunAsync(context, ChildInput("TICKET-1"));

        Assert.Equal(Enumerable.Range(0, snapshots.Count), snapshots.Select(snapshot => snapshot.Sequence));
    }

    /// <summary>
    /// A top-level run must be untouched: its sequence-0 slot belongs to the Host factory, and a
    /// second writer for it would overwrite the pre-start projection.
    /// </summary>
    [Fact]
    public async Task RunAsync_TopLevelRun_WritesNoSequenceZeroSnapshot()
    {
        var snapshots = new List<ExecutionSnapshot>();
        var context = Harness(snapshots);
        var input = new GraphOrchestratorInput { Definition = OrchestrationFixtures.ThreeArtifactChain(), EngagementId = "eng-1" };

        await orchestrator.RunAsync(context, input);

        Assert.DoesNotContain(snapshots, snapshot => snapshot.Sequence == 0);
        Assert.All(snapshots, snapshot => Assert.Null(snapshot.WorkItemId));
    }

    /// <summary>A one-shot run still records its mode — the projection answers "what kind of run is this?" for every run.</summary>
    [Fact]
    public async Task RunAsync_TopLevelRun_RecordsItsOneShotMode()
    {
        var snapshots = new List<ExecutionSnapshot>();
        var context = Harness(snapshots);
        var input = new GraphOrchestratorInput { Definition = OrchestrationFixtures.ThreeArtifactChain(), EngagementId = "eng-1" };

        await orchestrator.RunAsync(context, input);

        Assert.All(snapshots, snapshot => Assert.Equal(ExecutionMode.OneShot, snapshot.ExecutionMode));
    }

    /// <summary>The point of the change: two children of one engagement are told apart in the projection.</summary>
    [Fact]
    public async Task RunAsync_TwoChildrenOfOneEngagement_AreDistinguishableInTheProjection()
    {
        var first = new List<ExecutionSnapshot>();
        var second = new List<ExecutionSnapshot>();

        await orchestrator.RunAsync(Harness(first), ChildInput("TICKET-1"));
        await orchestrator.RunAsync(Harness(second), ChildInput("TICKET-2"));

        Assert.Equal("TICKET-1", first[0].WorkItemId);
        Assert.Equal("TICKET-2", second[0].WorkItemId);
        Assert.Equal(first[0].EngagementId, second[0].EngagementId);
    }

    private static GraphOrchestratorInput ChildInput(string workItemId) => new()
    {
        Definition = OrchestrationFixtures.DispatcherModeChain(),
        EngagementId = "eng-1",
        WorkItemId = workItemId,
        RunId = $"run-{workItemId}",
    };

    private static FakeTaskOrchestrationContext Harness(List<ExecutionSnapshot> snapshots)
    {
        var context = new FakeTaskOrchestrationContext();
        context.ActivityHandlers[WorkflowActivityNames.AgentTaskActivity] = activityInput =>
            new AgentTaskActivity(new FakeAgentTaskActivityPipeline()).RunAsync(new FakeTaskActivityContext(), (AgentTaskActivityInput)activityInput!).GetAwaiter().GetResult();
        context.ActivityHandlers[WorkflowActivityNames.SnapshotStateActivity] = activityInput =>
        {
            var snapshot = (ExecutionSnapshot)activityInput!;
            snapshots.Add(snapshot);
            return new SnapshotActivityResponse { SnapshotId = $"{snapshot.ExecutionId}:{snapshot.Sequence:D6}" };
        };
        context.ActivityHandlers[WorkflowActivityNames.ArtifactStateActivity] = activityInput =>
            new ArtifactStateActivityResponse { SectionRef = $"{((ArtifactStateActivityRequest)activityInput!).ArtifactKey}:v{((ArtifactStateActivityRequest)activityInput!).Version}" };
        context.ActivityHandlers[WorkflowActivityNames.ConsolidateAuditActivity] = activityInput =>
            AuditFixtures.SignedRecord((ConsolidateAuditInput)activityInput!);
        return context;
    }
}
