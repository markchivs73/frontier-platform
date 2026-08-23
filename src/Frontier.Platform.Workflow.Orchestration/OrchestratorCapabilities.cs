using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// What <see cref="GraphOrchestrator"/> can actually execute — the single source of truth the
/// runtime enforces and the design surface advertises (ADR-DC7, S13.7h).
///
/// Before S13.7h these were two independent lists: <c>EnsureSupported</c> hard-coded the runtime's
/// set while the designer's schema offered every declared <see cref="NodeType"/>. A designer could
/// therefore be handed a <c>parallel</c> node, have it pass validation clean, publish it, and only
/// discover at execution that the orchestrator refuses it — a permanent failure. Keeping one list
/// here means widening runtime support (e.g. S13.7c's <c>mcp_tool</c>) moves the design surface in
/// the same edit; the two cannot drift apart again.
/// </summary>
public static class OrchestratorCapabilities
{
    /// <summary>
    /// Node types the Phase-1 orchestrator executes. Everything else is rejected by
    /// <c>GraphOrchestratorSteps.EnsureSupported</c> as a permanent contract violation.
    /// </summary>
    public static IReadOnlyList<NodeType> SupportedNodeTypes { get; } = [NodeType.AgentTask, NodeType.HumanGate, NodeType.Decision, NodeType.McpTool];

    /// <summary>Whether the orchestrator can execute <paramref name="nodeType"/>.</summary>
    public static bool Supports(NodeType nodeType) => SupportedNodeTypes.Contains(nodeType);
}
