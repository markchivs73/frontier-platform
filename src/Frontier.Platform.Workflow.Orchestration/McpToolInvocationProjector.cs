using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Projects an execution's completed <c>mcp_tool</c> steps into <see cref="McpToolInvocation"/>s
/// (ADR-PA41): the signed audit's record of each deterministic tool call and the contract hashes
/// that validated it (ADR-E27 D15). DTF-side evidence, read from the final snapshot as the timeline
/// is. A step recorded before steps named their tool carries no <see cref="StepCompletion.ToolRef"/>
/// and has no entry; its <c>task_completed</c> event stays on the timeline.
/// </summary>
internal static class McpToolInvocationProjector
{
    /// <summary>
    /// Maps every MCP step that names its tool, in recorded order; <see langword="null"/> when there are
    /// none, so the record of an execution with no MCP step keeps the bytes it has always had.
    /// </summary>
    internal static IReadOnlyList<McpToolInvocation>? Project(IReadOnlyList<StepCompletion> steps)
    {
        var invocations = steps.Where(IsAuditable).Select(ToMcpToolInvocation).ToArray();
        return invocations.Length == 0 ? null : invocations;
    }

    /// <summary>Whether <paramref name="step"/> is an MCP step that names the tool it called.</summary>
    internal static bool IsAuditable(StepCompletion step) => step.NodeType == NodeType.McpTool && step.ToolRef is not null;

    /// <summary>Maps one MCP step; the step's empty output contract name (a node that names none) becomes <see langword="null"/>.</summary>
    internal static McpToolInvocation ToMcpToolInvocation(StepCompletion step) => new()
    {
        CorrelationId = step.CorrelationId,
        NodeId = step.NodeId,
        ArtifactKey = step.ArtifactKey,
        ToolRef = step.ToolRef!,
        InputContractType = step.InputContractType,
        InputHash = step.InputHash,
        OutputContractType = step.OutputContractType.Length == 0 ? null : step.OutputContractType,
        OutputHash = step.OutputHash,
        InputContractHash = step.InputContractHash,
        OutputContractHash = step.OutputContractHash,
        Simulated = step.Simulated,
        CompletedAtUtc = step.CompletedAtUtc,
    };
}
