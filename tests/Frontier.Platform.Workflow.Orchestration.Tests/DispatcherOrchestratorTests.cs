using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S6.10 tests for the <see cref="DispatcherOrchestrator"/> eternal router (doc 00 §4.4, ADR-E8),
/// extended at S13.22.
/// <para>
/// <b>What changed here at S13.22.</b> These tests previously asserted
/// <c>NotSupportedException</c> and read the captured child inputs from the wreckage: the fake
/// threw on <c>ContinueAsNew</c>, and the dispatcher's loop has no other terminating branch, so an
/// exception was the only way out. The fake now records the generation boundary instead, so the
/// body returns normally and the same behaviours are asserted directly. The attribution
/// assertions themselves are unchanged.
/// </para>
/// </summary>
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

    /// <summary>
    /// <b>The mode guard is a contract violation, not an operational fault.</b> It threw
    /// <c>InvalidOperationException</c> (<c>DispatcherOrchestrator.cs:33</c>) while the mirror-image
    /// guard in <c>GraphOrchestratorSteps.EnsureSupported</c> threw
    /// <see cref="ContractViolationException"/> for the same class of mistake — a definition whose
    /// declared mode does not match the orchestrator running it.
    /// <para>
    /// One vocabulary, because the distinction is load-bearing rather than cosmetic: hard invariant
    /// 7 makes contract violations <em>permanent</em> failures that are never retried, and that
    /// classification is read off the exception type. A wrong-mode definition retried against the
    /// same wall is the retry-into-a-permanent-failure shape the invariant exists to forbid.
    /// </para>
    /// </summary>
    [Fact]
    public async Task RunAsync_OneShotDefinition_ThrowsContractViolationException()
    {
        var context = new FakeTaskOrchestrationContext();
        var input = new GraphOrchestratorInput
        {
            Definition = OrchestrationFixtures.ThreeArtifactChain(executionMode: ExecutionMode.OneShot),
            EngagementId = "eng-1",
        };

        var exception = await Assert.ThrowsAsync<ContractViolationException>(() => orchestrator.RunAsync(context, input));

        Assert.Contains(ExecutionMode.Dispatcher.Name, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The event name is a wire contract shared with the ingest surface that raises it, so it is a constant rather than a literal in the loop (the ADR-CR1 refresh-name precedent).</summary>
    [Fact]
    public void WorkItemEventName_IsTheAdrE8ContractName() =>
        Assert.Equal("WorkItem", DispatcherOrchestrator.WorkItemEventName);

    /// <summary>ADR-E8/S13.19: per-item attribution survives the spawn — the work item's directing human wins.</summary>
    [Fact]
    public async Task RunAsync_WorkItemWithDirectedBy_ThreadsItIntoChildInitiatedBy()
    {
        var context = new FakeTaskOrchestrationContext();
        context.ExternalEvents[DispatcherOrchestrator.WorkItemEventName] = new WorkItem
        {
            WorkItemId = "SUB-001",
            Payload = DispatcherFixtures.Payload(),
            DirectedBy = "user:oid-supplier",
        };
        var input = new GraphOrchestratorInput
        {
            Definition = OrchestrationFixtures.ThreeArtifactChain(executionMode: ExecutionMode.Dispatcher),
            EngagementId = "eng-1",
            InitiatedBy = "user:oid-dispatcher-starter",
        };

        await orchestrator.RunAsync(context, input);

        var child = Assert.IsType<GraphOrchestratorInput>(context.SubOrchestratorInputs[0]);
        Assert.Equal("SUB-001", child.WorkItemId);
        Assert.Equal("user:oid-supplier", child.InitiatedBy);   // work item's directing human wins
    }

    [Fact]
    public async Task RunAsync_WorkItemWithoutDirectedBy_FallsBackToDispatcherInitiator()
    {
        var context = new FakeTaskOrchestrationContext();
        context.ExternalEvents[DispatcherOrchestrator.WorkItemEventName] = new WorkItem
        {
            WorkItemId = "SUB-002",
            Payload = DispatcherFixtures.Payload(),
        };
        var input = new GraphOrchestratorInput
        {
            Definition = OrchestrationFixtures.ThreeArtifactChain(executionMode: ExecutionMode.Dispatcher),
            EngagementId = "eng-1",
            InitiatedBy = "user:oid-dispatcher-starter",
        };

        await orchestrator.RunAsync(context, input);

        var child = Assert.IsType<GraphOrchestratorInput>(context.SubOrchestratorInputs[0]);
        Assert.Equal("user:oid-dispatcher-starter", child.InitiatedBy);
    }

    /// <summary>
    /// The child runs the graph, not another dispatcher. Pinned because the dispatcher hands the
    /// child its <em>own</em> pinned definition (whose mode is <c>dispatcher</c>) — the thing that
    /// makes <c>EnsureSupported</c>'s work-item carve-out necessary rather than optional.
    /// </summary>
    [Fact]
    public async Task RunAsync_SpawnsTheGraphOrchestrator()
    {
        var context = new FakeTaskOrchestrationContext();
        context.ExternalEvents[DispatcherOrchestrator.WorkItemEventName] = new WorkItem
        {
            WorkItemId = "SUB-003",
            Payload = DispatcherFixtures.Payload(),
        };

        await orchestrator.RunAsync(context, DispatcherFixtures.Input());

        Assert.Equal(WorkflowActivityNames.GraphOrchestrator, context.SubOrchestratorNames[0]);
    }
}
