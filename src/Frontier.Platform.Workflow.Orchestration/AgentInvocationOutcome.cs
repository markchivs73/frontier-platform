using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// <see cref="IAgentInvoker.InvokeAsync"/>'s per-invocation result: the agent's reply exactly as it
/// arrived, plus the provider's reported <see cref="UsageDetails"/> (S5.3, doc 05 §3) and the
/// attribution an agent target reports (ADR-PA27).
/// </summary>
public sealed record AgentInvocationOutcome
{
    /// <summary>The agent's final reply text, unparsed. The engine binds it to the node's output contract.</summary>
    public required string Output { get; init; }

    /// <summary>Token usage reported by the provider for this turn, or <see langword="null"/> if unavailable.</summary>
    public required UsageDetails? Usage { get; init; }

    /// <summary>
    /// MCP tools this invocation called, extracted from the response's message history (ADR-CD6,
    /// S9.25). <c>[]</c> when the node declared no tools or the model never called one.
    /// </summary>
    public required IReadOnlyList<ToolCall> ToolCalls { get; init; }

    /// <summary>
    /// The hash of the <b>pinned</b> card snapshot an agent target was called against (ADR-PA27 attribution);
    /// <see langword="null"/> for a model. Only the invoker knows it, because the card lives in the consumer's registry.
    /// </summary>
    public string? CardHash { get; init; }

    /// <summary>The remote agent's own id for this invocation, the A2A task id (ADR-PA27 addendum); <see langword="null"/> for a model.</summary>
    public string? RemoteTaskId { get; init; }
}

/// <summary>
/// <see cref="AgentInvocationDispatcher.InvokeAsync"/>'s result: the reply bound to the node's output
/// contract and validated, the invoker's usage and attribution, and the wall-clock
/// <see cref="LatencyMs"/> of the call (S5.3).
/// </summary>
public sealed record AgentInvocationResult
{
    /// <summary>
    /// The validated reply as it is stored: a CLR contract's canonical-profile bytes, or a registry
    /// contract's reply exactly as received (ADR-E27 D15).
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>The SHA-256 hex hash of <see cref="Payload"/>'s RFC 8785 (JCS) form.</summary>
    public required string Hash { get; init; }

    /// <summary>Token usage reported by the provider for this turn, or <see langword="null"/> if unavailable.</summary>
    public required UsageDetails? Usage { get; init; }

    /// <summary>MCP tools this invocation called (ADR-CD6, S9.25).</summary>
    public required IReadOnlyList<ToolCall> ToolCalls { get; init; }

    /// <summary>Wall-clock duration of the MAF invocation.</summary>
    public required long LatencyMs { get; init; }

    /// <summary>The pinned card hash an agent target reported (ADR-PA27); <see langword="null"/> for a model.</summary>
    public string? CardHash { get; init; }

    /// <summary>The remote agent's task id for this invocation (ADR-PA27 addendum); <see langword="null"/> for a model.</summary>
    public string? RemoteTaskId { get; init; }
}
