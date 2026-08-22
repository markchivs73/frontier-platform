using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Execution mode of a <see cref="WorkflowDefinition"/> (doc 00 §4.4, ADR-E8).
/// Serializes as a snake_case string, identical to a standard enum (doc 00 §3.5).
/// </summary>
public sealed class ExecutionMode : SmartEnum<ExecutionMode>
{
    /// <summary>Run the graph to completion once (doc 00 §4.1 — the default).</summary>
    public static readonly ExecutionMode OneShot = new("one_shot");

    /// <summary>Thin eternal router consuming work items via sub-orchestrations (doc 00 §4.4).</summary>
    public static readonly ExecutionMode Dispatcher = new("dispatcher");

    private ExecutionMode(string name)
        : base(name)
    {
    }
}
