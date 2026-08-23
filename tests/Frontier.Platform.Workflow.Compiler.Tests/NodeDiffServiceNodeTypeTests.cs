using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S9.24: <see cref="NodeDiffService"/>'s private <c>GetNodeKey</c> switch has one arm per
/// <see cref="WorkflowNode"/> subtype plus a fallback for any type it doesn't special-case
/// (<see cref="ContextInjectionNode"/>, the eighth kind) — the original suite exercised only
/// <see cref="AgentTaskNode"/>. Same-type-different-key-field is required per node so the
/// modification check reaches <c>GetNodeKey</c> rather than short-circuiting on a type change.
/// </summary>
public sealed class NodeDiffServiceNodeTypeTests
{
    private static WorkflowDefinition DefinitionWith(WorkflowNode node) => new()
    {
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        EngagementType = "test-type",
        Name = "Test",
        Nodes = [node],
        Edges = [],
        DefinitionHash = "hash",
        Mode = ExecutionMode.OneShot,
    };

    private static void AssertModified(WorkflowNode from, WorkflowNode to)
    {
        var diff = _new().Compute(DefinitionWith(from), DefinitionWith(to));
        Assert.Contains(from.NodeId, diff.NodesModified);
    }

    private static NodeDiffService _new() => new();

    [Fact]
    public void GetNodeKey_HumanGateNode_TimeoutChangeIsDetected() =>
        AssertModified(Gate(30), Gate(60));

    [Fact]
    public void GetNodeKey_DecisionNode_PredicateChangeIsDetected() =>
        AssertModified(Decision("a > 1"), Decision("a > 2"));

    [Fact]
    public void GetNodeKey_ParallelNode_SameKeyOnBranchChange_IsNotDetected()
    {
        // ParallelNode's key is NodeType-only (doc-recorded Phase 1 limitation) — branch
        // list changes are invisible to this diff; pins the actual (if surprising) behaviour.
        var diff = _new().Compute(
            DefinitionWith(Parallel(["b1"])),
            DefinitionWith(Parallel(["b1", "b2"]) with { NodeId = "node-1" }));

        Assert.DoesNotContain("node-1", diff.NodesModified);
    }

    [Fact]
    public void GetNodeKey_LoopNode_MaxIterationsChangeIsDetected() =>
        AssertModified(Loop(3), Loop(5));

    [Fact]
    public void GetNodeKey_McpToolNode_ToolRefChangeIsDetected() =>
        AssertModified(Tool("tool-a"), Tool("tool-b"));

    [Fact]
    public void GetNodeKey_CascadeCheckNode_SameKeyRegardlessOfTriggerArtifacts_IsNotDetected()
    {
        // CascadeCheckNode's key is NodeType-only; pins the current fallback behaviour.
        var diff = _new().Compute(
            DefinitionWith(Cascade(["scope"])),
            DefinitionWith(Cascade(["scope", "pricing"]) with { NodeId = "node-1" }));

        Assert.DoesNotContain("node-1", diff.NodesModified);
    }

    // fromNode.NodeType != toNode.NodeType (first operand of the || true) — every other test in this
    // file keeps the same NodeType and varies a key field; this is the only case where the same
    // NodeId's type itself changes, short-circuiting before GetNodeKey (S9.24 branch-coverage gap).
    [Fact]
    public void GetNodeKey_NodeTypeChangedForSameId_IsDetectedAsModified() =>
        AssertModified(Gate(30), Loop(3) with { NodeId = "node-1" });

    [Fact]
    public void GetNodeKey_UnhandledNodeType_FallsBackToNodeTypeOnly()
    {
        // ContextInjectionNode has no explicit switch arm — hits the `_ =>` fallback.
        // Two structurally different instances share the same fallback key (NodeType),
        // so the diff reports no modification despite different ContextRequest contents.
        var diff = _new().Compute(
            DefinitionWith(ContextInjection("field-a")),
            DefinitionWith(ContextInjection("field-b") with { NodeId = "node-1" }));

        Assert.DoesNotContain("node-1", diff.NodesModified);
    }

    private static HumanGateNode Gate(int timeoutMinutes) => new()
    {
        NodeId = "node-1",
        GateKind = GateKind.Business,
        ApproverRoles = ["business-approver"],
        PromptTemplate = "Approve?",
        TimeoutMinutes = timeoutMinutes,
    };

#pragma warning disable CS0618 // Legacy string-predicate wire shape stays covered until the phase boundary removes it (S13.7j).
    private static DecisionNode Decision(string predicate) => new()
    {
        NodeId = "node-1",
        Predicate = predicate,
        DefaultBranchNodeId = "node-2",
    };
#pragma warning restore CS0618

    private static ParallelNode Parallel(IReadOnlyList<string> branchNodeIds) => new()
    {
        NodeId = "node-1",
        BranchNodeIds = branchNodeIds,
        JoinNodeId = "node-join",
    };

    private static LoopNode Loop(int maxIterations) => new()
    {
        NodeId = "node-1",
        BodyNodeId = "node-body",
        MaxIterations = maxIterations,
    };

    private static McpToolNode Tool(string toolRef) => new()
    {
        NodeId = "node-1",
        ToolRef = toolRef,
        TimeoutSeconds = 30,
        IdempotencyKeySpec = "spec",
    };

    private static CascadeCheckNode Cascade(IReadOnlyList<string> triggerArtifactKeys) => new()
    {
        NodeId = "node-1",
        TriggerArtifactKeys = triggerArtifactKeys,
    };

#pragma warning disable CS0618 // Obsolete by design (ADR-CR1) — still a valid WorkflowNode subtype for GetNodeKey's fallback arm.
    private static ContextInjectionNode ContextInjection(string dynamicField) => new()
    {
        NodeId = "node-1",
        ContextRequest = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "deep-reasoning",
            BaselineComponents = [],
            DynamicFields = [dynamicField],
        },
    };
#pragma warning restore CS0618
}
