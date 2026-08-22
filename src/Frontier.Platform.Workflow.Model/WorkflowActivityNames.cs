namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Canonical DTF orchestration/activity name strings (doc 00 §4 node→primitive
/// mapping). <c>GraphOrchestrator</c> (Orchestration) calls these activities by
/// name; the activities themselves are implemented in CascadeLogic, ArtifactState,
/// and Orchestration. Subsystem libraries may not reference each other (library-
/// boundaries skill), so this shared, zero-dependency name set is the contract
/// that keeps the caller and the <c>[DurableTask(...)]</c>-decorated implementation
/// in agreement without a direct reference.
/// </summary>
public static class WorkflowActivityNames
{
    /// <summary>The <c>GraphOrchestrator</c> orchestration (Orchestration, S2.2).</summary>
    public const string GraphOrchestrator = "GraphOrchestrator";

    /// <summary>The <c>DispatcherOrchestrator</c> orchestration for event-consuming workflows (Orchestration, S6.10, doc 00 §4.4, ADR-E8).</summary>
    public const string DispatcherOrchestrator = "DispatcherOrchestrator";

    /// <summary>The stubbed agent-task activity for <see cref="AgentTaskNode"/> (Orchestration, S2.2).</summary>
    public const string AgentTaskActivity = "AgentTaskActivity";

    /// <summary>One deterministic MCP tool call for <see cref="McpToolNode"/> (Orchestration, S13.7c, doc 00 §3.2).</summary>
    public const string InvokeMcpToolActivity = "InvokeMcpToolActivity";

    /// <summary>Derives the section dependency graph and downstream set (CascadeLogic, doc 03 §4).</summary>
    public const string EvaluateCascadeActivity = "EvaluateCascadeActivity";

    /// <summary>Writes the <see cref="ExecutionSnapshot"/> projection to Cosmos (ArtifactState, doc 02 §5).</summary>
    public const string SnapshotStateActivity = "SnapshotStateActivity";

    /// <summary>Writes a section's version history and <c>current</c> pointer to Cosmos (ArtifactState, doc 02 §2-3).</summary>
    public const string ArtifactStateActivity = "ArtifactStateActivity";

    /// <summary>Opens a <see cref="HumanGateNode"/>'s approval request and persists it to the <c>approvals</c> container (Hitl, doc 06 §4, §9).</summary>
    public const string RequestApprovalActivity = "RequestApprovalActivity";

    /// <summary>Marks a pending approval request escalated on gate timeout (Hitl, doc 06 §7).</summary>
    public const string EscalateApprovalActivity = "EscalateApprovalActivity";

    /// <summary>Repoints rolled-back sections' <c>current</c> documents at their approved version refs (ArtifactState, doc 06 §6).</summary>
    public const string RestoreArtifactsActivity = "RestoreArtifactsActivity";

    /// <summary>Consolidates DTF history and staged telemetry into a signed audit record, the orchestrator's final activity on completion (Audit, doc 05 §4, §8).</summary>
    public const string ConsolidateAuditActivity = "ConsolidateAuditActivity";
}
