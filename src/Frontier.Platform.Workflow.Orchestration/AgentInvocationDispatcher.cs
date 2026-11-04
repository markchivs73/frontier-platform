using System.Diagnostics;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Contracts;
using Frontier.Platform.Resilience;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Calls <see cref="IAgentInvoker"/> for an agent node and binds the reply to the node's output
/// contract (doc 00 §4.3 step 5; ADR-E27 D7, D8). The engine, not the invoker, builds the
/// structured-output format a model target is asked for, and the engine deserializes and validates
/// whatever came back: a reply that is not valid JSON, or fails the contract, is a permanent
/// <see cref="ContractViolationException"/>. The invoker call — and only that call, not the binding — runs
/// through the inner Polly pipeline for the resolved target's <c>(provider, modelId)</c> (doc 10 §5, §6), so
/// provider weather is retried there and the target's breaker sees every call. Times the call, retries
/// included, for <see cref="AgentInvocationResult.LatencyMs"/> (S5.3).
/// </summary>
internal sealed class AgentInvocationDispatcher
{
    private readonly IAgentInvoker invoker;
    private readonly IContractTypeRegistry registry;
    private readonly IContractValidator validator;
    private readonly IResiliencePolicyProvider policyProvider;

    /// <summary>
    /// Constructs a dispatcher over <paramref name="invoker"/>, called through <paramref name="policyProvider"/>'s
    /// endpoint pipelines, binding CLR replies via <paramref name="registry"/> and registry replies via <paramref name="validator"/>.
    /// </summary>
    public AgentInvocationDispatcher(IAgentInvoker invoker, IContractTypeRegistry registry, IContractValidator validator, IResiliencePolicyProvider policyProvider)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(policyProvider);

        this.invoker = invoker;
        this.registry = registry;
        this.validator = validator;
        this.policyProvider = policyProvider;
    }

    /// <summary>
    /// Invokes the agent with the output format for its contract, then binds and validates the reply:
    /// against <paramref name="outputContract"/> when the node names a registry contract, otherwise as
    /// <paramref name="outputContractType"/>'s CLR type. A registry reply is validated under the run's
    /// pinned <paramref name="budgets"/>, or the configured ones when it pinned none (ADR-PA43). The call runs
    /// under <paramref name="profileName"/>, the node's resilience profile.
    /// </summary>
    internal async Task<AgentInvocationResult> InvokeAsync(string profileName, string outputContractType, ContractSnapshot? outputContract, ContractBudgets? budgets, AgentInvocationRequest request, CancellationToken ct)
    {
        var formatted = request with { ResponseFormat = ResponseFormatFor(outputContractType, outputContract, request.Target) };
        var pipeline = policyProvider.GetPipeline(profileName, request.Target.Provider, request.Target.TargetId);

        var stopwatch = Stopwatch.StartNew();
        var outcome = await pipeline.ExecuteAsync(async attemptCt => await invoker.InvokeAsync(formatted, attemptCt).ConfigureAwait(false), ct).ConfigureAwait(false);
        stopwatch.Stop();

        var (payload, hash) = outputContract is null ? BindClr(outputContractType, outcome.Output) : BindRegistry(outputContract, outcome.Output, budgets);
        return new AgentInvocationResult
        {
            Payload = payload,
            Hash = hash,
            Usage = outcome.Usage,
            ToolCalls = outcome.ToolCalls,
            LatencyMs = stopwatch.ElapsedMilliseconds,
            CardHash = outcome.CardHash,
            RemoteTaskId = outcome.RemoteTaskId,
        };
    }

    /// <summary>
    /// The format a model target is asked to follow; <see langword="null"/> for an agent target, whose
    /// protocol has none, and for a registry schema that cannot be lowered (ADR-E27 D7).
    /// </summary>
    internal ChatResponseFormat? ResponseFormatFor(string outputContractType, ContractSnapshot? outputContract, Frontier.Platform.ModelRoleConfig.ChainEntry target) =>
        target is not Frontier.Platform.ModelRoleConfig.ModelEntry ? null
        : outputContract is not null ? RegistryOutputSchema.For(outputContract)
        : CanonicalOutputSchema.For(registry.Resolve(outputContractType));

    /// <summary>A CLR reply: deserialized and validated, stored as its canonical-profile bytes, hashed as JCS.</summary>
    internal (string Payload, string Hash) BindClr(string outputContractType, string output) =>
        OutputPayloadBuilder.Build(registry.DeserializeAndValidate(outputContractType, output), registry.Resolve(outputContractType));

    /// <summary>A registry reply: validated against the pinned contract and stored as received; only its hash is canonicalised (ADR-E27 D15).</summary>
    internal (string Payload, string Hash) BindRegistry(ContractSnapshot outputContract, string output, ContractBudgets? budgets) =>
        (output, JsonCanonicalizer.Hash(RegistryContractCheck.Ensure(validator, outputContract, output, budgets)));
}
