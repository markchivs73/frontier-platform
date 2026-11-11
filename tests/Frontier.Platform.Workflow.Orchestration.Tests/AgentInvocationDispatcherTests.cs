using Polly;
using Polly.Retry;
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
        var dispatcher = new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, new FakeResiliencePolicyProvider());
        var request = new AgentInvocationRequest
        {
            Instructions = "instructions",
            Prompt = "prompt",
            ModelId = "claude-fable-5",
            Target = Frontier.Platform.ModelRoleConfig.Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0],
            MaxOutputTokens = 100,
        };

        var result = await dispatcher.InvokeAsync("llm-default", nameof(SummaryArtifact), null, null, request, CancellationToken.None);

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
        var dispatcher = new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, new FakeResiliencePolicyProvider());
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

        var result = await dispatcher.InvokeAsync("llm-default", nameof(SummaryArtifact), null, null, request, CancellationToken.None);

        Assert.Equal("sha256:pinned-card", result.CardHash);
        Assert.Equal("resp_0139", result.RemoteTaskId);
        Assert.Null(invoker.ReceivedFormat);
    }

    /// <summary>ADR-E27 D8: the engine binds the reply, so a reply that is not JSON is a permanent contract violation.</summary>
    [Fact]
    public async Task InvokeAsync_AReplyThatIsNotJson_IsAContractViolation()
    {
        var dispatcher = new AgentInvocationDispatcher(new FakeAgentInvoker(null, output: "Here is your summary!"), new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, new FakeResiliencePolicyProvider());

        await Assert.ThrowsAsync<Frontier.Platform.Abstractions.ContractViolationException>(
            () => dispatcher.InvokeAsync("llm-default", nameof(SummaryArtifact), null, null, ModelRequest(), CancellationToken.None));
    }

    /// <summary>ADR-E27 D8: the engine validates the bound reply; the invoker no longer does.</summary>
    [Fact]
    public async Task InvokeAsync_AReplyThatFailsItsContract_IsAContractViolation()
    {
        var invalid = new SummaryArtifact { Title = "", Objectives = [] };
        var dispatcher = new AgentInvocationDispatcher(new FakeAgentInvoker(invalid), new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, new FakeResiliencePolicyProvider());

        await Assert.ThrowsAsync<Frontier.Platform.Abstractions.ContractViolationException>(
            () => dispatcher.InvokeAsync("llm-default", nameof(SummaryArtifact), null, null, ModelRequest(), CancellationToken.None));
    }

    /// <summary>
    /// S13.274: a model target's output format is built from the output contract type, so an unknown one is a
    /// contract violation before the invoker is called.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_AnUnknownOutputContractTypeForAModel_IsAContractViolationBeforeTheCall()
    {
        var invoker = new FakeAgentInvoker(new SummaryArtifact { Title = "Scope", Objectives = ["objective"] });
        var dispatcher = new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, new FakeResiliencePolicyProvider());

        var exception = await Assert.ThrowsAsync<Frontier.Platform.Abstractions.ContractViolationException>(
            () => dispatcher.InvokeAsync("llm-default", "RecordExtract", null, null, ModelRequest(), CancellationToken.None));

        Assert.Equal("RecordExtract", exception.ContractType);
        Assert.Null(invoker.ReceivedRequest);
    }

    /// <summary>S13.274: an agent target is asked for no format, so an unknown output contract type fails when its reply is bound.</summary>
    [Fact]
    public async Task InvokeAsync_AnUnknownOutputContractTypeForAnAgent_IsAContractViolationWhenBound()
    {
        var invoker = new CardHashReportingInvoker(new SummaryArtifact { Title = "Scope", Objectives = ["objective"] }, "sha256:pinned-card");
        var dispatcher = new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, new FakeResiliencePolicyProvider());
        var request = ModelRequest() with
        {
            Target = new Frontier.Platform.ModelRoleConfig.AgentEntry
            {
                Provider = Frontier.Platform.ModelRoleConfig.AgentEntry.A2aProvider,
                Currency = "USD",
                ResourceName = "com.azure.foundry/echo",
                ResourceVersion = "1.0",
                CostPerInvocation = 0.02m,
            },
        };

        var exception = await Assert.ThrowsAsync<Frontier.Platform.Abstractions.ContractViolationException>(
            () => dispatcher.InvokeAsync("llm-default", "RecordExtract", null, null, request, CancellationToken.None));

        Assert.Equal("RecordExtract", exception.ContractType);
        Assert.Equal("Unknown contract type 'RecordExtract'; the workload's contract set does not name it.", Assert.Single(exception.Violations));
        Assert.Null(invoker.ReceivedFormat);
    }

    private static AgentInvocationRequest ModelRequest() => new()
    {
        Instructions = "instructions",
        Prompt = "prompt",
        ModelId = "claude-fable-5",
        Target = Frontier.Platform.ModelRoleConfig.Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0],
        MaxOutputTokens = 100,
    };

    /// <summary>S13.217: the invoker call runs through the endpoint pipeline for the target's (provider, modelId), under the node's profile.</summary>
    [Fact]
    public async Task InvokeAsync_RunsTheCallThroughTheTargetsEndpointPipeline()
    {
        var policy = new FakeResiliencePolicyProvider();
        var dispatcher = new AgentInvocationDispatcher(new FakeAgentInvoker(new SummaryArtifact { Title = "Scope", Objectives = ["objective"] }), new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, policy);
        var request = ModelRequest();

        await dispatcher.InvokeAsync("llm-interactive", nameof(SummaryArtifact), null, null, request, CancellationToken.None);

        Assert.Equal([("llm-interactive", request.Target.Provider, request.Target.TargetId)], policy.RequestedEndpoints);
    }

    /// <summary>S13.217: a failure the pipeline handles is retried inside the activity, so the reply still binds.</summary>
    [Fact]
    public async Task InvokeAsync_APipelineThatRetries_RetriesAFailedCall()
    {
        var retrying = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 1, Delay = TimeSpan.Zero }).Build();
        var invoker = new FailOnceInvoker(new FakeAgentInvoker(new SummaryArtifact { Title = "Scope", Objectives = ["objective"] }));
        var dispatcher = new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, new FakeResiliencePolicyProvider(retrying));

        var result = await dispatcher.InvokeAsync("llm-default", nameof(SummaryArtifact), null, null, ModelRequest(), CancellationToken.None);

        Assert.Equal(2, invoker.Calls);
        Assert.Contains("\"title\":\"Scope\"", result.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_NullPolicyProvider_Throws()
    {
        var invoker = new FakeAgentInvoker(new SummaryArtifact { Title = "Scope", Objectives = ["objective"] });

        Assert.Throws<ArgumentNullException>(() => new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, null!));
    }

    [Fact]
    public void Constructor_NullInvoker_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentInvocationDispatcher(null!, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, new FakeResiliencePolicyProvider()));
    }

    [Fact]
    public void Constructor_NullValidator_Throws()
    {
        var invoker = new FakeAgentInvoker(new SummaryArtifact { Title = "Scope", Objectives = ["objective"] });

        Assert.Throws<ArgumentNullException>(() => new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance), null!, new FakeResiliencePolicyProvider()));
    }

    [Fact]
    public void Constructor_NullRegistry_Throws()
    {
        var invoker = new FakeAgentInvoker(new SummaryArtifact { Title = "Scope", Objectives = ["objective"] });

        Assert.Throws<ArgumentNullException>(() => new AgentInvocationDispatcher(invoker, null!, TestContractValidator.Instance, new FakeResiliencePolicyProvider()));
    }

    /// <summary>An invoker whose first call fails with a provider 503, then delegates to <paramref name="inner"/>.</summary>
    private sealed class FailOnceInvoker(IAgentInvoker inner) : IAgentInvoker
    {
        internal int Calls { get; private set; }

        public Task<AgentInvocationOutcome> InvokeAsync(AgentInvocationRequest request, CancellationToken ct) =>
            ++Calls == 1 ? throw new HttpRequestException("unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable) : inner.InvokeAsync(request, ct);
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
