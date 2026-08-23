using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.2 tests for <see cref="AgentInvocationDispatcher"/>.</summary>
public sealed class AgentInvocationDispatcherTests
{
    [Fact]
    public async Task InvokeAsync_ResolvesOutputTypeAndReturnsInvokerResult()
    {
        var scope = new SummaryArtifact { Title = "Scope", Objectives = ["objective"] };
        var invoker = new FakeAgentInvoker(scope);
        var dispatcher = new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance));
        var request = new AgentInvocationRequest
        {
            Instructions = "instructions",
            Prompt = "prompt",
            ModelId = "claude-fable-5",
            MaxOutputTokens = 100,
        };

        var result = await dispatcher.InvokeAsync(nameof(SummaryArtifact), request, CancellationToken.None);

        Assert.Same(scope, result.Result);
        Assert.Same(request, invoker.ReceivedRequest);
        Assert.True(result.LatencyMs >= 0);
    }

    [Fact]
    public void Constructor_NullInvoker_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentInvocationDispatcher(null!, new ContractTypeRegistry(TestContractSet.Instance)));
    }

    [Fact]
    public void Constructor_NullRegistry_Throws()
    {
        var invoker = new FakeAgentInvoker(new SummaryArtifact { Title = "Scope", Objectives = ["objective"] });

        Assert.Throws<ArgumentNullException>(() => new AgentInvocationDispatcher(invoker, null!));
    }
}
