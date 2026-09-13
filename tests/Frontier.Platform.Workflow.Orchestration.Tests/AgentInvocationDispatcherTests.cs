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
            Target = Frontier.Platform.ModelRoleConfig.Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0],
            MaxOutputTokens = 100,
        };

        var result = await dispatcher.InvokeAsync(nameof(SummaryArtifact), request, CancellationToken.None);

        Assert.Same(scope, result.Result);
        Assert.Same(request, invoker.ReceivedRequest);
        Assert.True(result.LatencyMs >= 0);
    }

    [Fact]
    public async Task InvokeAsync_InvokerReportsACardHash_BridgesItAcrossTheReflectionBoundary()
    {
        // ADR-PA27: only the invoker knows the pinned card it called; the dispatcher must carry it to the pipeline.
        var scope = new SummaryArtifact { Title = "Scope", Objectives = ["objective"] };
        var dispatcher = new AgentInvocationDispatcher(new CardHashReportingInvoker(scope, "sha256:pinned-card"), new ContractTypeRegistry(TestContractSet.Instance));
        var request = new AgentInvocationRequest
        {
            Instructions = "instructions",
            Prompt = "prompt",
            ModelId = "com.azure.foundry/echo",
            Target = new Frontier.Platform.ModelRoleConfig.AgentEntry
            {
                Provider = Frontier.Platform.ModelRoleConfig.AgentEntry.A2aProvider,
                Currency = "USD",
                ResourceName = "com.azure.foundry/echo",
                ResourceVersion = "1.0",
                CostPerInvocation = 0.02m,
            },
            MaxOutputTokens = 0,
        };

        var result = await dispatcher.InvokeAsync(nameof(SummaryArtifact), request, CancellationToken.None);

        Assert.Equal("sha256:pinned-card", result.CardHash);
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

    /// <summary>An invoker standing in for an agent path: it reports the pinned card hash it called against.</summary>
    private sealed class CardHashReportingInvoker(Frontier.Platform.Abstractions.IVersionedContract result, string cardHash) : IAgentInvoker
    {
        public Task<AgentInvocationOutcome<TOutput>> InvokeAsync<TOutput>(AgentInvocationRequest request, CancellationToken ct)
            where TOutput : Frontier.Platform.Abstractions.IVersionedContract =>
            Task.FromResult(new AgentInvocationOutcome<TOutput> { Result = (TOutput)result, Usage = null, ToolCalls = [], CardHash = cardHash });
    }
}
