using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>Shared node/edge/definition factories for the S9.30 rule tests.</summary>
internal static class S930Fixtures
{
    internal static WorkflowDefinition Build(
        IReadOnlyList<WorkflowNode> nodes,
        IReadOnlyList<WorkflowEdge>? edges = null,
        ExecutionMode? mode = null) => new()
    {
        WorkflowId = "wf-test",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Test Workflow",
        Nodes = nodes,
        Edges = edges ?? [],
        DefinitionHash = "hash",
        Mode = mode ?? ExecutionMode.OneShot,
    };

    internal static AgentTaskNode Agent(
        string id,
        string role = "deep-reasoning",
        string instructionsRef = "instructions/gen-scope.md",
        string inputContract = "BriefArtifact",
        string outputContract = "SummaryArtifact",
        string? sectionKey = "scope",
        IReadOnlyList<string>? baselineComponents = null,
        IReadOnlyList<string>? dynamicFields = null,
        RetryPolicySpec? retry = null) => new()
    {
        NodeId = id,
        ArtifactKey = sectionKey,
        Retry = retry,
        Role = role,
        InstructionsRef = instructionsRef,
        InputContractType = inputContract,
        OutputContractType = outputContract,
        ContextRequest = new ContextRequest
        {
            EngagementId = "engagement-id",
            AgentRole = role,
            BaselineComponents = baselineComponents ?? [],
            DynamicFields = dynamicFields ?? [],
        },
    };

    internal static HumanGateNode Gate(
        string id,
        string? rollbackTo = null,
        int timeoutMinutes = 0,
        IReadOnlyList<string>? approverRoles = null) => new()
    {
        NodeId = id,
        GateKind = GateKind.Business,
        ApproverRoles = approverRoles ?? ["business-approver"],
        PromptTemplate = "Review and decide.",
        TimeoutMinutes = timeoutMinutes,
        RollbackToNodeId = rollbackTo,
    };

    internal static McpToolNode Mcp(
        string id,
        string toolRef = "io.frontier.demo/autotask/update_ticket",
        int timeoutSeconds = 30,
        string idempotencyKeySpec = "ticket:{ticket_id}") => new()
    {
        NodeId = id,
        ToolRef = toolRef,
        TimeoutSeconds = timeoutSeconds,
        IdempotencyKeySpec = idempotencyKeySpec,
    };

    internal static ParallelNode Parallel(string id, IReadOnlyList<string> branches, string joinNodeId) => new()
    {
        NodeId = id,
        BranchNodeIds = branches,
        JoinNodeId = joinNodeId,
    };

#pragma warning disable CS0618 // Legacy string-predicate wire shape stays covered until the phase boundary removes it (S13.7j).
    internal static DecisionNode Decision(string id, string defaultBranchNodeId, string predicate = "budget > 0") => new()
    {
        NodeId = id,
        Predicate = predicate,
        DefaultBranchNodeId = defaultBranchNodeId,
    };
#pragma warning restore CS0618

    /// <summary>A decision carrying a doc 14 §6 branch tree (S13.7j).</summary>
    internal static DecisionNode DecisionWithBranches(string id, string defaultBranchNodeId, params ConditionalBranch[] branches) => new()
    {
        NodeId = id,
        DefaultBranchNodeId = defaultBranchNodeId,
        Branches = branches,
    };

    internal static LoopNode Loop(string id, string bodyNodeId = "body", int maxIterations = 3) => new()
    {
        NodeId = id,
        BodyNodeId = bodyNodeId,
        MaxIterations = maxIterations,
    };

    internal static WorkflowEdge Control(string from, string to, string? condition = null) => new()
    {
        FromNodeId = from,
        ToNodeId = to,
        Kind = EdgeKind.Control,
        Condition = condition,
    };

    internal static WorkflowEdge Data(string from, string to, string? contractType) => new()
    {
        FromNodeId = from,
        ToNodeId = to,
        Kind = EdgeKind.Data,
        ContractType = contractType,
    };
}
