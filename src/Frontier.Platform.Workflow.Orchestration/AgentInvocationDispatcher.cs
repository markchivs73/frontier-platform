using System.Diagnostics;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
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

    /// <summary>Constructs a dispatcher over <paramref name="invoker"/>, binding replies via <paramref name="registry"/>.</summary>
    public AgentInvocationDispatcher(IAgentInvoker invoker, IContractTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(registry);

        this.invoker = invoker;
        this.registry = registry;
    }

    /// <summary>Invokes the agent with the output format for <paramref name="outputContractType"/>, then binds and validates its reply.</summary>
    internal async Task<AgentInvocationResult> InvokeAsync(string outputContractType, AgentInvocationRequest request, CancellationToken ct)
    {
        var formatted = request with { ResponseFormat = ResponseFormatFor(outputContractType, request.Target) };

        var stopwatch = Stopwatch.StartNew();
        var outcome = await invoker.InvokeAsync(formatted, ct).ConfigureAwait(false);
        stopwatch.Stop();

        return new AgentInvocationResult
        {
            Result = registry.DeserializeAndValidate(outputContractType, outcome.Output),
            Usage = outcome.Usage,
            ToolCalls = outcome.ToolCalls,
            LatencyMs = stopwatch.ElapsedMilliseconds,
            CardHash = outcome.CardHash,
            RemoteTaskId = outcome.RemoteTaskId,
        };
    }

    /// <summary>
    /// The format a model target is asked to follow; <see langword="null"/> for an agent target, whose
    /// protocol has none (ADR-E27 D7).
    /// </summary>
    internal ChatResponseFormat? ResponseFormatFor(string outputContractType, Frontier.Platform.ModelRoleConfig.ChainEntry target) =>
        target is Frontier.Platform.ModelRoleConfig.ModelEntry ? CanonicalOutputSchema.For(registry.Resolve(outputContractType)) : null;
}
