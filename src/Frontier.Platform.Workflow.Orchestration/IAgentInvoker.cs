using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Runs one MAF agent turn and returns its output bound directly to the requested
/// <typeparamref name="TOutput"/> contract (doc 00 §4.3 step 5, ADR-AG1: MAF's
/// <c>AgentResponse&lt;T&gt;.Result</c> direct-POCO binding). Implementations never
/// validate <typeparamref name="TOutput"/> — that is <see cref="IContractTypeRegistry"/>'s
/// job once the typed result is returned.
/// </summary>
public interface IAgentInvoker
{
    /// <summary>
    /// Invokes the agent described by <paramref name="request"/> and deserializes its
    /// response as <typeparamref name="TOutput"/>, alongside the provider's reported
    /// token usage (S5.3). Throws <see cref="ContractViolationException"/>
    /// (permanent, doc 09) if the model's response is not valid JSON for
    /// <typeparamref name="TOutput"/>.
    /// </summary>
    Task<AgentInvocationOutcome<TOutput>> InvokeAsync<TOutput>(AgentInvocationRequest request, CancellationToken ct)
        where TOutput : IVersionedContract;
}
