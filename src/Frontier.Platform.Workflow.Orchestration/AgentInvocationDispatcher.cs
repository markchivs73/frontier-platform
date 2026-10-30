using System.Diagnostics;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Contracts;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Calls <see cref="IAgentInvoker"/> for an agent node and binds the reply to the node's output
/// contract (doc 00 §4.3 step 5; ADR-E27 D7, D8). The engine, not the invoker, builds the
/// structured-output format a model target is asked for, and the engine deserializes and validates
/// whatever came back: a reply that is not valid JSON, or fails the contract, is a permanent
/// <see cref="ContractViolationException"/>. Times the call for <see cref="AgentInvocationResult.LatencyMs"/> (S5.3).
/// </summary>
internal sealed class AgentInvocationDispatcher
{
    private readonly IAgentInvoker invoker;
    private readonly IContractTypeRegistry registry;
    private readonly IContractValidator validator;

    /// <summary>Constructs a dispatcher over <paramref name="invoker"/>, binding CLR replies via <paramref name="registry"/> and registry replies via <paramref name="validator"/>.</summary>
    public AgentInvocationDispatcher(IAgentInvoker invoker, IContractTypeRegistry registry, IContractValidator validator)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(validator);

        this.invoker = invoker;
        this.registry = registry;
        this.validator = validator;
    }

    /// <summary>
    /// Invokes the agent with the output format for its contract, then binds and validates the reply:
    /// against <paramref name="outputContract"/> when the node names a registry contract, otherwise as
    /// <paramref name="outputContractType"/>'s CLR type.
    /// </summary>
    internal async Task<AgentInvocationResult> InvokeAsync(string outputContractType, ContractSnapshot? outputContract, AgentInvocationRequest request, CancellationToken ct)
    {
        var formatted = request with { ResponseFormat = ResponseFormatFor(outputContractType, outputContract, request.Target) };

        var stopwatch = Stopwatch.StartNew();
        var outcome = await invoker.InvokeAsync(formatted, ct).ConfigureAwait(false);
        stopwatch.Stop();

        var (payload, hash) = outputContract is null ? BindClr(outputContractType, outcome.Output) : BindRegistry(outputContract, outcome.Output);
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
    internal (string Payload, string Hash) BindRegistry(ContractSnapshot outputContract, string output) =>
        (output, JsonCanonicalizer.Hash(RegistryContractCheck.Ensure(validator, outputContract, output)));
}
