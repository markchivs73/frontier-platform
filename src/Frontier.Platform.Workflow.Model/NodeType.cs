using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Discriminator for the eight <see cref="WorkflowNode"/> subtypes (doc 00 §3.2).
/// Serializes as a snake_case string, identical to a standard enum (doc 00 §3.5).
/// </summary>
public sealed class NodeType : SmartEnum<NodeType>
{
    /// <summary>Invokes a MAF agent (<see cref="AgentTaskNode"/>).</summary>
    public static readonly NodeType AgentTask = new("agent_task");

    /// <summary>Human approval gate (<see cref="HumanGateNode"/>).</summary>
    public static readonly NodeType HumanGate = new("human_gate");

    /// <summary>Deterministic branch on structured data predicates (<see cref="DecisionNode"/>).</summary>
    public static readonly NodeType Decision = new("decision");

    /// <summary>Fan-out/fan-in over independent branches (<see cref="ParallelNode"/>).</summary>
    public static readonly NodeType Parallel = new("parallel");

    /// <summary>Bounded iteration (<see cref="LoopNode"/>).</summary>
    public static readonly NodeType Loop = new("loop");

    /// <summary>External system call via an MCP connector (<see cref="McpToolNode"/>).</summary>
    public static readonly NodeType McpTool = new("mcp_tool");

    /// <summary>Deprecated; dynamic-tier refresh is signal-driven (ADR-CR1) (<see cref="ContextInjectionNode"/>).</summary>
    public static readonly NodeType ContextInjection = new("context_injection");

    /// <summary>Evaluates downstream impact after a section update (<see cref="CascadeCheckNode"/>).</summary>
    public static readonly NodeType CascadeCheck = new("cascade_check");

    private NodeType(string name)
        : base(name)
    {
    }
}
