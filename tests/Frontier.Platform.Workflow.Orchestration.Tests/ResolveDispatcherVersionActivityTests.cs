using Frontier.Platform.Workflow.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.18 (ADR-E15 D2) — the dispatcher's version rollover at the <c>ContinueAsNew</c> generation
/// boundary.
/// <para>
/// <b>The plan entry called this "a three-line edit at the existing
/// <c>context.ContinueAsNew(input)</c>". It is not.</b> <c>DispatcherOrchestrator.cs:61</c> hands
/// the <em>original</em> input straight back, so an eternal dispatcher runs its launch-day
/// definition forever; there is no activity, no activity-name constant, and no port to resolve a
/// version through. All three are new surface.
/// </para>
/// <para>
/// <b>Why an activity at all.</b> Resolving the current published version honouring doc 16 §8
/// per-engagement pins is a store read, and hard invariant 2 forbids a body reading any store.
/// The boundary is the only correct moment: a definition is immutable once published (invariant 6)
/// and a running generation stays pinned to the one it started with, so the version can only move
/// where the generation does.
/// </para>
/// <para>
/// <b>Declared here, implemented by the consumer</b> (S13.22 Q3), exactly as
/// <see cref="IDynamicContextContentProducer"/> is: what a published version <em>is</em>, and what
/// an engagement's pin means, is the consumer's knowledge (doc 16 §8, ADR-E7) — S13.43 built that
/// resolver there. The engine declares the port and registers the activity, and deliberately does
/// not register a default: a default here would let a misconfigured deployment roll a dispatcher
/// onto the wrong definition instead of failing to start.
/// </para>
/// </summary>
public sealed class ResolveDispatcherVersionActivityTests
{
    private static ResolveDispatcherVersionRequest Request() => new()
    {
        EngagementId = "E2E::Acme::HQ",
        WorkflowId = "three-section-chain",
        CurrentDefinitionVersion = 1,
    };

    /// <summary>The activity asks the consumer's resolver and returns what it resolved, unchanged.</summary>
    [Fact]
    public async Task RunAsync_ResolvesThroughThePortAndReturnsTheDefinition()
    {
        var resolved = OrchestrationFixtures.ThreeArtifactChain(ExecutionMode.Dispatcher) with { DefinitionVersion = 7 };
        var resolver = new FakeVersionResolver(resolved);
        var activity = new ResolveDispatcherVersionActivity(resolver);

        var result = await activity.RunAsync(new FakeTaskActivityContext(), Request());

        Assert.Equal("E2E::Acme::HQ", resolver.ReceivedEngagementId);
        Assert.Equal("three-section-chain", resolver.ReceivedWorkflowId);
        Assert.Equal(1, resolver.ReceivedCurrentVersion);
        Assert.Equal(7, result.Definition!.DefinitionVersion);
    }

    /// <summary>
    /// <b>Retired with no successor.</b> Doc 16 §8's third bullet lets an engagement keep running a
    /// definition it is already pinned to; ADR-E15 D2 adds that the queue never stalls on a retired
    /// version. A null resolution is therefore "continue on the current version", never an error
    /// and never a halt — the retirement monitor alerts, the tickets keep flowing.
    /// </summary>
    [Fact]
    public async Task RunAsync_RetiredWithNoSuccessor_ResolvesToNull()
    {
        var activity = new ResolveDispatcherVersionActivity(new FakeVersionResolver(null));

        var result = await activity.RunAsync(new FakeTaskActivityContext(), Request());

        Assert.Null(result.Definition);
    }

    [Fact]
    public async Task RunAsync_NullInput_Throws()
    {
        var activity = new ResolveDispatcherVersionActivity(new FakeVersionResolver(null));

        await Assert.ThrowsAsync<ArgumentNullException>(() => activity.RunAsync(new FakeTaskActivityContext(), null!));
    }

    [Fact]
    public void Constructor_NullResolver_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ResolveDispatcherVersionActivity(null!));

    /// <summary>The engine registers the activity but never the port behind it (the S13.62 precedent).</summary>
    [Fact]
    public void AddFrontierWorkflowOrchestration_RegistersTheActivityButNotItsPort()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{OrchestrationOptions.ArtifactName}:SandboxMode"] = "false",
        }).Build();

        var registered = new ServiceCollection()
            .AddFrontierWorkflowOrchestration(configuration)
            .Select(descriptor => descriptor.ServiceType)
            .ToHashSet();

        Assert.Contains(typeof(ResolveDispatcherVersionActivity), registered);
        Assert.DoesNotContain(typeof(IDispatcherVersionResolver), registered);
    }

    /// <summary>The activity name is the caller/implementation contract across the library boundary.</summary>
    [Fact]
    public void ActivityName_IsDeclaredInTheSharedNameSet() =>
        Assert.Equal("ResolveDispatcherVersionActivity", WorkflowActivityNames.ResolveDispatcherVersionActivity);

    /// <summary>
    /// The boundary call happens once per generation, immediately before <c>ContinueAsNew</c>, and
    /// carries the engagement and workflow the pin is resolved against. Resolving anywhere else —
    /// per work item, say — would re-read the store on every ticket and could move the definition
    /// mid-generation, unpinning a running instance from the definition its history recorded.
    /// </summary>
    [Fact]
    public void Rollover_IsResolvedExactlyOnce_AtTheGenerationBoundary()
    {
        var harness = new DispatcherSpawnTests.DispatcherHarness();
        harness.Start();

        harness.RaiseWorkItems(DispatcherOrchestrator.ContinueAsNewThreshold);

        var request = Assert.Single(harness.ResolveRequests);
        Assert.Equal("E2E::Acme::HQ", request.EngagementId);
        Assert.Equal("three-section-chain", request.WorkflowId);
        Assert.Equal(1, request.CurrentDefinitionVersion);
    }

    /// <summary>Mid-generation, nothing is resolved: the running generation stays pinned.</summary>
    [Fact]
    public void BeforeTheBoundary_NoVersionIsResolved()
    {
        var harness = new DispatcherSpawnTests.DispatcherHarness();
        harness.Start();

        harness.RaiseWorkItems(3);

        Assert.Empty(harness.ResolveRequests);
    }

    /// <summary>What the activity resolved is what the next generation runs.</summary>
    [Fact]
    public void ResolvedDefinition_IsWhatContinueAsNewCarries()
    {
        var rolled = OrchestrationFixtures.ThreeArtifactChain(ExecutionMode.Dispatcher) with { DefinitionVersion = 12 };
        var harness = new DispatcherSpawnTests.DispatcherHarness(rollover: rolled);
        harness.Start();

        harness.RaiseWorkItems(DispatcherOrchestrator.ContinueAsNewThreshold);

        var next = Assert.IsType<GraphOrchestratorInput>(harness.Context.ContinueAsNewCalls[0].Input);
        Assert.Equal(12, next.Definition.DefinitionVersion);
    }

    /// <summary>Retired with no successor: the next generation continues on the definition it already had.</summary>
    [Fact]
    public void NoSuccessorResolved_ContinuesOnTheCurrentDefinition()
    {
        var harness = new DispatcherSpawnTests.DispatcherHarness(rollover: null);
        harness.Start();

        harness.RaiseWorkItems(DispatcherOrchestrator.ContinueAsNewThreshold);

        var next = Assert.IsType<GraphOrchestratorInput>(harness.Context.ContinueAsNewCalls[0].Input);
        Assert.Equal(1, next.Definition.DefinitionVersion);
        Assert.Equal(ExecutionMode.Dispatcher, next.Definition.Mode);
    }

    /// <summary>Records what the engine asked for and returns a fixed resolution.</summary>
    private sealed class FakeVersionResolver(WorkflowDefinition? resolved) : IDispatcherVersionResolver
    {
        public string? ReceivedEngagementId { get; private set; }
        public string? ReceivedWorkflowId { get; private set; }
        public int ReceivedCurrentVersion { get; private set; }

        public Task<WorkflowDefinition?> ResolveAsync(string engagementId, string workflowId, int currentVersion, CancellationToken ct)
        {
            ReceivedEngagementId = engagementId;
            ReceivedWorkflowId = workflowId;
            ReceivedCurrentVersion = currentVersion;
            return Task.FromResult(resolved);
        }
    }
}
