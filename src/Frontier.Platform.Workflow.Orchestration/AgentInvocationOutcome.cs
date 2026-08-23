using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// <see cref="IAgentInvoker.InvokeAsync{TOutput}"/>'s per-invocation result: the typed
/// contract MAF bound directly (ADR-AG1) plus the provider's reported
/// <see cref="UsageDetails"/> (S5.3, doc 05 §3), needed by
/// <see cref="AgentTaskActivityPipeline"/> to build an <see cref="AuditTelemetryRecord"/>.
/// </summary>
internal sealed record AgentInvocationOutcome<TOutput>
    where TOutput : IVersionedContract
{
    /// <summary>
    /// The agent's response bound directly to <typeparamref name="TOutput"/>. Public (despite
    /// the enclosing type being <see langword="internal"/>) because
    /// <see cref="AgentInvocationDispatcher"/> reads it via reflection across the
    /// <see cref="IAgentInvoker.InvokeAsync{TOutput}"/> generic boundary (ADR-AG1) —
    /// <c>Type.GetProperty</c>'s default <see cref="System.Reflection.BindingFlags"/> only
    /// finds public members.
    /// </summary>
    public required TOutput Result { get; init; }

    /// <summary>Token usage reported by the provider for this turn, or <see langword="null"/> if unavailable. Public for the same reflection reason as <see cref="Result"/>.</summary>
    public required UsageDetails? Usage { get; init; }

    /// <summary>
    /// MCP tools this invocation called, extracted from the MAF response's message history
    /// (ADR-CD6, S9.25). <c>[]</c> when the node declared no tools or the model never
    /// called one. Public for the same reflection reason as <see cref="Result"/>.
    /// </summary>
    public required IReadOnlyList<ToolCall> ToolCalls { get; init; }
}

/// <summary>
/// <see cref="AgentInvocationDispatcher.InvokeAsync"/>'s untyped result: an
/// <see cref="AgentInvocationOutcome{TOutput}"/>'s <c>Result</c>/<c>Usage</c>/<c>ToolCalls</c>
/// bridged across the reflection boundary (ADR-AG1), plus the wall-clock
/// <see cref="LatencyMs"/> of the MAF call (S5.3).
/// </summary>
internal sealed record AgentInvocationResult
{
    /// <summary>The validated output contract MAF returned.</summary>
    internal required IVersionedContract Result { get; init; }

    /// <summary>Token usage reported by the provider for this turn, or <see langword="null"/> if unavailable.</summary>
    internal required UsageDetails? Usage { get; init; }

    /// <summary>MCP tools this invocation called (ADR-CD6, S9.25).</summary>
    internal required IReadOnlyList<ToolCall> ToolCalls { get; init; }

    /// <summary>Wall-clock duration of the MAF invocation.</summary>
    internal required long LatencyMs { get; init; }
}
