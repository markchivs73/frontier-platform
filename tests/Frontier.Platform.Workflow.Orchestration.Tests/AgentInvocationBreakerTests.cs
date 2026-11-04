using System.Text;
using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Resilience;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.217: <see cref="AgentInvocationDispatcher"/> over the real Resilience registrations (doc 10 §5, §6).
/// The invoker call runs through the target's tracked breaker; the reply binding does not, so a reply
/// that fails its contract never counts toward that breaker.
/// </summary>
public sealed class AgentInvocationBreakerTests : IDisposable
{
    private const int TripThreshold = 10;

    private readonly ServiceProvider services = new ServiceCollection().AddFrontierResilience().BuildServiceProvider();

    private IResiliencePolicyProvider Policies => services.GetRequiredService<IResiliencePolicyProvider>();

    private ICircuitStateProvider Circuits => services.GetRequiredService<ICircuitStateProvider>();

    [Fact]
    public async Task InvokeAsync_RepliesThatFailTheirContract_LeaveTheBreakerClosed()
    {
        var dispatcher = Dispatcher(new FakeAgentInvoker(null, output: "Here is your summary!"));
        var request = ModelRequest();

        for (var i = 0; i < TripThreshold; i++)
        {
            await Assert.ThrowsAsync<ContractViolationException>(() => dispatcher.InvokeAsync("llm-default", nameof(SummaryArtifact), null, null, request, CancellationToken.None));
        }

        Assert.Equal(CircuitState.Closed, Circuits.GetState(request.Target.Provider, request.Target.TargetId));
    }

    /// <summary>The control for the test above: the same number of failures thrown by the invoker itself opens the breaker.</summary>
    [Fact]
    public async Task InvokeAsync_InvokerFailures_OpenTheTargetsBreaker()
    {
        var dispatcher = Dispatcher(new ThrowingInvoker());
        var request = ModelRequest();

        for (var i = 0; i < TripThreshold; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.InvokeAsync("llm-default", nameof(SummaryArtifact), null, null, request, CancellationToken.None));
        }

        Assert.Equal(CircuitState.Open, Circuits.GetState(request.Target.Provider, request.Target.TargetId));
        await Assert.ThrowsAsync<BrokenCircuitException>(() => dispatcher.InvokeAsync("llm-default", nameof(SummaryArtifact), null, null, request, CancellationToken.None));
    }

    /// <summary><see cref="AgentInvocationResult.LatencyMs"/> times the whole pipeline call, so a retried attempt's time is included.</summary>
    [Fact]
    public async Task InvokeAsync_ARetriedCall_CountsTheFailedAttemptInItsLatency()
    {
        var retrying = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 1, Delay = TimeSpan.Zero }).Build();
        var invoker = new SlowFailOnceInvoker(new FakeAgentInvoker(new SummaryArtifact { Title = "Scope", Objectives = ["objective"] }), TimeSpan.FromMilliseconds(100));
        var dispatcher = new AgentInvocationDispatcher(invoker, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, new FakeResiliencePolicyProvider(retrying));

        var result = await dispatcher.InvokeAsync("llm-default", nameof(SummaryArtifact), null, null, ModelRequest(), CancellationToken.None);

        Assert.True(result.LatencyMs >= 90, $"latency {result.LatencyMs}ms excludes the failed attempt");
    }

    /// <summary><c>retry_profile</c> is omitted when null, so an input recorded before the field keeps its bytes.</summary>
    [Fact]
    public void AgentTaskActivityInput_RecordedWithoutARetryProfile_ReadsNullAndKeepsItsBytes()
    {
        const string preChange = """{"node_id":"n-1","role":"analyst","instructions_ref":"i.md","input_contract_type":"In","output_contract_type":"Out","correlation_id":"c-1","engagement_id":"eng-1","context_request":{"schema_version":"1.0","engagement_id":"eng-1","agent_role":"analyst","baseline_components":[],"dynamic_fields":[],"requires_real_time":false,"real_time_sources":[]},"execution_id":"x","tool_refs":[]}""";

        var rehydrated = JsonSerializer.Deserialize<AgentTaskActivityInput>(preChange, CanonicalProfile.Options)!;

        Assert.Null(rehydrated.RetryProfile);
        Assert.Equal(preChange, Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(rehydrated)));
        Assert.EndsWith(""","retry_profile":"llm-interactive"}""", Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(rehydrated with { RetryProfile = "llm-interactive" })), StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose() => services.Dispose();

    private AgentInvocationDispatcher Dispatcher(IAgentInvoker invoker) =>
        new(invoker, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance, Policies);

    private static AgentInvocationRequest ModelRequest() => new()
    {
        Instructions = "instructions",
        Prompt = "prompt",
        ModelId = "claude-fable-5",
        Target = Frontier.Platform.ModelRoleConfig.Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0],
        MaxOutputTokens = 100,
    };

    /// <summary>An invoker whose every call fails with an unclassified (so never retried) exception: one breaker sample per call.</summary>
    private sealed class ThrowingInvoker : IAgentInvoker
    {
        public Task<AgentInvocationOutcome> InvokeAsync(AgentInvocationRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("provider down");
    }

    /// <summary>An invoker whose first call waits <paramref name="delay"/> then fails, then delegates to <paramref name="inner"/>.</summary>
    private sealed class SlowFailOnceInvoker(IAgentInvoker inner, TimeSpan delay) : IAgentInvoker
    {
        private int calls;

        public async Task<AgentInvocationOutcome> InvokeAsync(AgentInvocationRequest request, CancellationToken ct)
        {
            if (++calls > 1)
            {
                return await inner.InvokeAsync(request, ct);
            }

            await Task.Delay(delay, ct);
            throw new HttpRequestException("unavailable");
        }
    }
}
