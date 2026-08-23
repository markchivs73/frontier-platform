using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Configurable <see cref="IAgentInvoker"/> test double for S4.2/S5.3/S9.25 dispatcher/pipeline tests.</summary>
internal sealed class FakeAgentInvoker(IVersionedContract result, UsageDetails? usage = null, IReadOnlyList<ToolCall>? toolCalls = null) : IAgentInvoker
{
    /// <summary>The most recent <see cref="AgentInvocationRequest"/> passed to <see cref="InvokeAsync{TOutput}"/>.</summary>
    internal AgentInvocationRequest? ReceivedRequest { get; private set; }

    public Task<AgentInvocationOutcome<TOutput>> InvokeAsync<TOutput>(AgentInvocationRequest request, CancellationToken ct)
        where TOutput : IVersionedContract
    {
        ReceivedRequest = request;
        return Task.FromResult(new AgentInvocationOutcome<TOutput> { Result = (TOutput)result, Usage = usage, ToolCalls = toolCalls ?? [] });
    }
}
