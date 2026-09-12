using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.22 — the branches of <see cref="DispatcherSteps"/> the behavioural suite does not reach,
/// and the one router behaviour that only appears once a child actually finishes.
/// </summary>
public sealed class DispatcherStepsTests
{
    /// <summary>
    /// A finished child leaves the outstanding set. This is what keeps the router's race a
    /// <em>wait</em>: a completed task left in the set makes every subsequent
    /// <c>Task.WhenAny</c> return immediately, turning the loop into a spin that burns a
    /// worker thread for the life of an eternal instance.
    /// </summary>
    [Fact]
    public void PruneFinishedChildren_DropsCompletedAndKeepsOutstanding()
    {
        var outstanding = new TaskCompletionSource<GraphOrchestratorResult>();
        var children = new List<Task<GraphOrchestratorResult>> { Task.FromResult(Result()), outstanding.Task };

        DispatcherSteps.PruneFinishedChildren(children);

        Assert.Equal([outstanding.Task], children);
    }

    /// <summary>
    /// A faulted child is dropped too, and its exception is observed on the way out. A child is an
    /// independent execution that records its own failure; an unobserved fault here would instead
    /// surface much later, at an unrelated moment, as a process-level unobserved task exception.
    /// </summary>
    [Fact]
    public void PruneFinishedChildren_DropsAFaultedChildAndObservesIt()
    {
        var failed = Task.FromException<GraphOrchestratorResult>(new InvalidOperationException("child failed"));
        var children = new List<Task<GraphOrchestratorResult>> { failed };

        DispatcherSteps.PruneFinishedChildren(children);

        Assert.Empty(children);
        Assert.NotNull(failed.Exception);
    }

    /// <summary>The router keeps routing after a child finishes — the pruning path, end to end.</summary>
    [Fact]
    public void AfterAChildCompletes_TheRouterStillSpawnsForLaterWorkItems()
    {
        var harness = new DispatcherSpawnTests.DispatcherHarness();
        harness.Start();

        harness.RaiseWorkItem("TICKET-1");
        harness.CompleteChild(0);
        harness.RaiseWorkItem("TICKET-2");

        Assert.Equal(["TICKET-1", "TICKET-2"], harness.ChildWorkItemIds);
        Assert.Equal(1, harness.CompletedChildren);
    }

    /// <summary>The rollover request is projected from the dispatcher's own pinned definition, never from a child's.</summary>
    [Fact]
    public void BuildResolveRequest_ProjectsTheEngagementAndThePinnedDefinition()
    {
        var request = DispatcherSteps.BuildResolveRequest(DispatcherFixtures.Input());

        Assert.Equal("E2E::Acme::HQ", request.EngagementId);
        Assert.Equal("three-section-chain", request.WorkflowId);
        Assert.Equal(1, request.CurrentDefinitionVersion);
    }

    /// <summary>The next generation carries the dispatcher's dynamic-context pin, and never a work item id — the router is not a child.</summary>
    [Fact]
    public void BuildNextGenerationInput_CarriesThePinAndNoWorkItem()
    {
        var next = DispatcherSteps.BuildNextGenerationInput(DispatcherFixtures.Input(), resolved: null);

        Assert.Null(next.WorkItemId);
        Assert.Equal(DispatcherFixtures.Epoch, next.DynamicContextEpoch);
        Assert.Equal(DispatcherFixtures.EpochHash, next.DynamicContextHash);
    }

    /// <summary>A resolved successor replaces the definition and nothing else.</summary>
    [Fact]
    public void BuildNextGenerationInput_ResolvedSuccessor_ReplacesOnlyTheDefinition()
    {
        var resolved = OrchestrationFixtures.ThreeArtifactChain(ExecutionMode.Dispatcher) with { DefinitionVersion = 5 };

        var next = DispatcherSteps.BuildNextGenerationInput(DispatcherFixtures.Input(), resolved);

        Assert.Equal(5, next.Definition.DefinitionVersion);
        Assert.Equal(DispatcherFixtures.DispatcherRunId, next.RunId);
        Assert.Equal("user:oid-dispatcher-starter", next.InitiatedBy);
    }

    /// <summary>Default state of the rollover result is "no successor" — the safe half of the contract, never "lookup failed".</summary>
    [Fact]
    public void ResolveDispatcherVersionResult_DefaultsToNoSuccessor() =>
        Assert.Null(new ResolveDispatcherVersionResult().Definition);

    private static GraphOrchestratorResult Result() => new()
    {
        CompletedSteps = [],
        ArtifactStatuses = new Dictionary<string, ArtifactStatus>(StringComparer.Ordinal),
    };
}
