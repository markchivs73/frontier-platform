using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// Configurable <see cref="IAgentInvoker"/> test double: replies with <paramref name="result"/>'s
/// canonical JSON, or with <paramref name="output"/> verbatim when given.
/// </summary>
internal sealed class FakeAgentInvoker(IVersionedContract? result, UsageDetails? usage = null, IReadOnlyList<ToolCall>? toolCalls = null, string? output = null) : IAgentInvoker
{
    /// <summary>The most recent <see cref="AgentInvocationRequest"/> passed to <see cref="InvokeAsync"/>.</summary>
    internal AgentInvocationRequest? ReceivedRequest { get; private set; }

    /// <summary>The reply text this invoker returns.</summary>
    internal string Output { get; } = output ?? System.Text.Json.JsonSerializer.Serialize(result, result!.GetType(), CanonicalProfile.Options);

    public Task<AgentInvocationOutcome> InvokeAsync(AgentInvocationRequest request, CancellationToken ct)
    {
        ReceivedRequest = request;
        return Task.FromResult(new AgentInvocationOutcome { Output = Output, Usage = usage, ToolCalls = toolCalls ?? [] });
    }
}
