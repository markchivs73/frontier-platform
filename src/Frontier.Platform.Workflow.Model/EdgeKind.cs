using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Distinguishes control-flow edges from data-dependency edges in a
/// <see cref="WorkflowEdge"/> (doc 00 §3.3). Serializes as a snake_case string,
/// identical to a standard enum (doc 00 §3.5).
/// </summary>
public sealed class EdgeKind : SmartEnum<EdgeKind>
{
    /// <summary>The edge represents ordering only; no typed payload flows along it.</summary>
    public static readonly EdgeKind Control = new("control");

    /// <summary>
    /// The edge declares that <c>ToNodeId</c> consumes <c>FromNodeId</c>'s output as
    /// <see cref="WorkflowEdge.ContractType"/>. The cascade dependency graph is derived
    /// from these edges at compile time (doc 00 §3.3, ADR-3).
    /// </summary>
    public static readonly EdgeKind Data = new("data");

    private EdgeKind(string name)
        : base(name)
    {
    }
}
