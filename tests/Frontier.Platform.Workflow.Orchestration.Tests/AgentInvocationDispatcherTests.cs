using Microsoft.Extensions.AI;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.2 tests for <see cref="AgentInvocationDispatcher"/>.</summary>
public sealed class AgentInvocationDispatcherTests
{
    [Fact]
    public async Task InvokeAsync_BindsTheReplyToTheOutputContract_AndAsksAModelForItsFormat()
    {
        var scope = new SummaryArtifact { Title = "Scope", Objectives = ["objective"] };
        var invoker = new FakeAgentInvoker(scope);
        var dispatcher = new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance);
        var request = new AgentInvocationRequest
        {
            Instructions = "instructions",
            Prompt = "prompt",
            ModelId = "claude-fable-5",
            Target = Frontier.Platform.ModelRoleConfig.Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0],
            MaxOutputTokens = 100,
        };

        var result = await dispatcher.InvokeAsync(nameof(SummaryArtifact), null, request, CancellationToken.None);

        var (payload, hash) = OutputPayloadBuilder.Build(scope, typeof(SummaryArtifact));
        Assert.Equal(payload, result.Payload);
        Assert.Equal(hash, result.Hash);
        Assert.Same(CanonicalOutputSchema.For<SummaryArtifact>(), invoker.ReceivedRequest!.ResponseFormat);
        Assert.Equal(request with { ResponseFormat = invoker.ReceivedRequest.ResponseFormat }, invoker.ReceivedRequest);
        Assert.True(result.LatencyMs >= 0);
    }

    [Fact]
    public async Task InvokeAsync_InvokerReportsACardHash_CarriesIt_AndAsksAnAgentForNoFormat()
    {
        // ADR-PA27: only the invoker knows the pinned card it called; the dispatcher must carry it to the pipeline.
        var scope = new SummaryArtifact { Title = "Scope", Objectives = ["objective"] };
        var invoker = new CardHashReportingInvoker(scope, "sha256:pinned-card");
        var dispatcher = new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance);
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

        var result = await dispatcher.InvokeAsync(nameof(SummaryArtifact), null, request, CancellationToken.None);

        Assert.Equal("sha256:pinned-card", result.CardHash);
        Assert.Equal("resp_0139", result.RemoteTaskId);
        Assert.Null(invoker.ReceivedFormat);
    }

    /// <summary>ADR-E27 D8: the engine binds the reply, so a reply that is not JSON is a permanent contract violation.</summary>
    [Fact]
    public async Task InvokeAsync_AReplyThatIsNotJson_IsAContractViolation()
    {
        var dispatcher = new AgentInvocationDispatcher(new FakeAgentInvoker(null, output: "Here is your summary!"), new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance);

        await Assert.ThrowsAsync<Frontier.Platform.Abstractions.ContractViolationException>(
            () => dispatcher.InvokeAsync(nameof(SummaryArtifact), null, ModelRequest(), CancellationToken.None));
    }

    /// <summary>ADR-E27 D8: the engine validates the bound reply; the invoker no longer does.</summary>
    [Fact]
    public async Task InvokeAsync_AReplyThatFailsItsContract_IsAContractViolation()
    {
        var invalid = new SummaryArtifact { Title = "", Objectives = [] };
        var dispatcher = new AgentInvocationDispatcher(new FakeAgentInvoker(invalid), new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance);

        await Assert.ThrowsAsync<Frontier.Platform.Abstractions.ContractViolationException>(
            () => dispatcher.InvokeAsync(nameof(SummaryArtifact), null, ModelRequest(), CancellationToken.None));
    }

    private static AgentInvocationRequest ModelRequest() => new()
    {
        Instructions = "instructions",
        Prompt = "prompt",
        ModelId = "claude-fable-5",
        Target = Frontier.Platform.ModelRoleConfig.Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0],
        MaxOutputTokens = 100,
    };

    [Fact]
    public void Constructor_NullInvoker_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentInvocationDispatcher(null!, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance));
    }

    [Fact]
    public void Constructor_NullValidator_Throws()
    {
        var invoker = new FakeAgentInvoker(new SummaryArtifact { Title = "Scope", Objectives = ["objective"] });

        Assert.Throws<ArgumentNullException>(() => new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance), null!));
    }

    [Fact]
    public void Constructor_NullRegistry_Throws()
    {
        var invoker = new FakeAgentInvoker(new SummaryArtifact { Title = "Scope", Objectives = ["objective"] });

        Assert.Throws<ArgumentNullException>(() => new AgentInvocationDispatcher(invoker, null!, TestContractValidator.Instance));
    }

    /// <summary>An invoker standing in for an agent path: it reports the pinned card hash it called against.</summary>
    private sealed class CardHashReportingInvoker(SummaryArtifact result, string cardHash) : IAgentInvoker
    {
        internal ChatResponseFormat? ReceivedFormat { get; private set; } = ChatResponseFormat.Json;

        public Task<AgentInvocationOutcome> InvokeAsync(AgentInvocationRequest request, CancellationToken ct)
        {
            ReceivedFormat = request.ResponseFormat;
            return Task.FromResult(new AgentInvocationOutcome
            {
                Output = System.Text.Json.JsonSerializer.Serialize(result, Frontier.Platform.Serialization.CanonicalProfile.Options),
                Usage = null,
                ToolCalls = [],
                CardHash = cardHash,
                RemoteTaskId = "resp_0139",
            });
        }
    }
}
