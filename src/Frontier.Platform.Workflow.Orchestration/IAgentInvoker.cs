using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Runs one agent turn and returns the reply as the agent sent it (doc 00 §4.3 step 5). Transport
/// only (ADR-E27 D8): the engine builds the structured-output format it asks for
/// (<see cref="AgentInvocationRequest.ResponseFormat"/>), then binds and validates the reply against
/// the node's output contract itself. An invoker never deserializes or validates the reply.
/// </summary>
public interface IAgentInvoker
{
    /// <summary>
    /// Invokes the agent described by <paramref name="request"/> and returns its final reply text,
    /// with the provider's reported token usage and the tools it called.
    /// </summary>
    Task<AgentInvocationOutcome> InvokeAsync(AgentInvocationRequest request, CancellationToken ct);
}
