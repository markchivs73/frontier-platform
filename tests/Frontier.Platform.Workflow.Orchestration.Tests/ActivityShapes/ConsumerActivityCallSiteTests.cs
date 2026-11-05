using System.Runtime.CompilerServices;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>
/// S13.185: the hand-listed <see cref="ActivityCatalogue.ConsumerImplemented"/> types are the ones
/// <see cref="GraphOrchestratorSteps"/>' call sites pass and await, so the snapshot records the wire
/// shapes the consumer's implementations really receive and return.
/// </summary>
public sealed class ConsumerActivityCallSiteTests
{
    [Fact]
    public async Task EveryConsumerCallSite_PassesAndAwaitsTheCataloguedTypes()
    {
        var context = new FakeTaskOrchestrationContext();
        foreach (var activity in ActivityCatalogue.ConsumerImplemented)
        {
            context.ActivityHandlers[activity.Name] = _ => RuntimeHelpers.GetUninitializedObject(activity.Output);
        }

        await CallEveryConsumerActivity(context);

        Assert.Equal(
            ActivityCatalogue.ConsumerImplemented.Select(a => new ActivityCall(a.Name, a.Input, a.Output)).OrderBy(c => c.Name, StringComparer.Ordinal),
            context.ActivityCalls.DistinctBy(c => c.Name).OrderBy(c => c.Name, StringComparer.Ordinal));
    }

    private static async Task CallEveryConsumerActivity(FakeTaskOrchestrationContext context)
    {
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain());
        var state = new GraphExecutionState { StartedAtUtc = OrchestrationFixtures.StartedAtUtc };
        await GraphOrchestratorSteps.EvaluateCascadeAsync(context, input.Definition, state, "scope");
        await GraphOrchestratorSteps.WriteArtifactVersionAsync(context, input, state, "scope", "content", "hash");
        await GraphOrchestratorSteps.WriteSnapshotAsync(context, input, state, ExecutionStatus.Running, currentNodeId: null, new FakeResiliencePolicyProvider());
        await GraphOrchestratorSteps.RestoreArtifactsAsync(context, input, new Dictionary<string, string> { ["scope"] = "scope-ref-v1" }, state);
    }
}
