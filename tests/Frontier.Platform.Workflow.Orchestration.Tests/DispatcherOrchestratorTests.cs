using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S6.10 tests for the <see cref="DispatcherOrchestrator"/> eternal router (doc 00 §4.4, ADR-E8).</summary>
public sealed class DispatcherOrchestratorTests
{
    private readonly DispatcherOrchestrator orchestrator = new();

    [Fact]
    public async Task RunAsync_NullContext_Throws()
    {
        var input = new GraphOrchestratorInput
        {
            Definition = OrchestrationFixtures.ThreeArtifactChain(executionMode: ExecutionMode.Dispatcher),
            EngagementId = "eng-1",
        };

        await Assert.ThrowsAsync<ArgumentNullException>(() => orchestrator.RunAsync(null!, input));
    }

    [Fact]
    public async Task RunAsync_NullInput_Throws()
    {
        var context = new FakeTaskOrchestrationContext();

        await Assert.ThrowsAsync<ArgumentNullException>(() => orchestrator.RunAsync(context, null!));
    }

    [Fact]
    public async Task RunAsync_OneShootDefinition_Throws()
    {
        var context = new FakeTaskOrchestrationContext();
        var input = new GraphOrchestratorInput
        {
            Definition = OrchestrationFixtures.ThreeArtifactChain(executionMode: ExecutionMode.OneShot),
            EngagementId = "eng-1",
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunAsync(context, input));
        Assert.Contains("ExecutionMode.Dispatcher", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WorkItemWithDirectedBy_ThreadsItIntoChildInitiatedBy()
    {
        // ADR-E8/S13.19: per-item attribution survives the spawn. The fake returns the same
        // WorkItem on every wait, so the loop spawns children up to the ContinueAsNew
        // threshold, where the fake's ContinueAsNew throws NotSupportedException — the loop
        // has no other exit, and by then every child input is captured for assertion.
        var context = new FakeTaskOrchestrationContext();
        context.ExternalEvents["WorkItem"] = new WorkItem
        {
            WorkItemId = "SUB-001",
            Payload = new { },
            DirectedBy = "user:oid-supplier",
        };
        var input = new GraphOrchestratorInput
        {
            Definition = OrchestrationFixtures.ThreeArtifactChain(executionMode: ExecutionMode.Dispatcher),
            EngagementId = "eng-1",
            InitiatedBy = "user:oid-dispatcher-starter",
        };

        await Assert.ThrowsAsync<NotSupportedException>(() => orchestrator.RunAsync(context, input));

        var child = Assert.IsType<GraphOrchestratorInput>(context.SubOrchestratorInputs[0]);
        Assert.Equal("SUB-001", child.WorkItemId);
        Assert.Equal("user:oid-supplier", child.InitiatedBy);   // work item's directing human wins
    }

    [Fact]
    public async Task RunAsync_WorkItemWithoutDirectedBy_FallsBackToDispatcherInitiator()
    {
        var context = new FakeTaskOrchestrationContext();
        context.ExternalEvents["WorkItem"] = new WorkItem { WorkItemId = "SUB-002", Payload = new { } };
        var input = new GraphOrchestratorInput
        {
            Definition = OrchestrationFixtures.ThreeArtifactChain(executionMode: ExecutionMode.Dispatcher),
            EngagementId = "eng-1",
            InitiatedBy = "user:oid-dispatcher-starter",
        };

        await Assert.ThrowsAsync<NotSupportedException>(() => orchestrator.RunAsync(context, input));

        var child = Assert.IsType<GraphOrchestratorInput>(context.SubOrchestratorInputs[0]);
        Assert.Equal("user:oid-dispatcher-starter", child.InitiatedBy);
    }
}
