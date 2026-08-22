using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Xunit;

namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class WorkflowNodeTests
{
    private static readonly IReadOnlyList<string> EmptyStringList = Array.Empty<string>();

    #region AgentTaskNode Tests

    [Fact]
    public void AgentTaskNode_WithAllProperties_IsConstructible()
    {
        var node = new AgentTaskNode
        {
            NodeId = "agent-1",
            Role = "analyst",
            InstructionsRef = "analyze-scope",
            InputContractType = "EngagementBriefSection",
            OutputContractType = "ScopeSection",
            ContextRequest = CreateContextRequest(),
            ArtifactKey = "scope",
            Retry = null
        };

        Assert.Equal("agent-1", node.NodeId);
        Assert.Equal("analyst", node.Role);
        Assert.Equal("analyze-scope", node.InstructionsRef);
        Assert.Equal("EngagementBriefSection", node.InputContractType);
        Assert.Equal("ScopeSection", node.OutputContractType);
        Assert.NotNull(node.ContextRequest);
        Assert.Equal("scope", node.ArtifactKey);
        Assert.Null(node.Retry);
    }

    [Fact]
    public void AgentTaskNode_NodeTypeProperty_ReturnsAgentTask()
    {
        var node = new AgentTaskNode
        {
            NodeId = "agent-1",
            Role = "analyst",
            InstructionsRef = "analyze",
            InputContractType = "Input",
            OutputContractType = "Output",
            ContextRequest = CreateContextRequest()
        };

        Assert.Equal(NodeType.AgentTask, node.NodeType);
    }

    [Fact]
    public void AgentTaskNode_WithRetryPolicy_IsConstructible()
    {
        var retry = new RetryPolicySpec { ProfileName = "standard", MaxAttemptsOverride = 3 };
        var node = new AgentTaskNode
        {
            NodeId = "agent-1",
            Role = "analyst",
            InstructionsRef = "analyze",
            InputContractType = "Input",
            OutputContractType = "Output",
            ContextRequest = CreateContextRequest(),
            Retry = retry
        };

        Assert.NotNull(node.Retry);
        Assert.Equal("standard", node.Retry.ProfileName);
    }

    [Fact]
    public void AgentTaskNode_Equality_ConsidersAllProperties()
    {
        var node1 = new AgentTaskNode
        {
            NodeId = "agent-1",
            Role = "analyst",
            InstructionsRef = "analyze",
            InputContractType = "Input",
            OutputContractType = "Output",
            ContextRequest = CreateContextRequest()
        };
        var node2 = new AgentTaskNode
        {
            NodeId = "agent-1",
            Role = "analyst",
            InstructionsRef = "analyze",
            InputContractType = "Input",
            OutputContractType = "Output",
            ContextRequest = CreateContextRequest()
        };

        Assert.Equal(node1, node2);
    }

    [Fact]
    public void AgentTaskNode_Inequality_WhenRoleDiffers()
    {
        var node1 = new AgentTaskNode
        {
            NodeId = "agent-1",
            Role = "analyst",
            InstructionsRef = "analyze",
            InputContractType = "Input",
            OutputContractType = "Output",
            ContextRequest = CreateContextRequest()
        };
        var node2 = new AgentTaskNode
        {
            NodeId = "agent-1",
            Role = "designer",
            InstructionsRef = "analyze",
            InputContractType = "Input",
            OutputContractType = "Output",
            ContextRequest = CreateContextRequest()
        };

        Assert.NotEqual(node1, node2);
    }

    [Fact]
    public void AgentTaskNode_WithMinimalProperties_IsConstructible()
    {
        var node = new AgentTaskNode
        {
            NodeId = "agent-1",
            Role = "analyst",
            InstructionsRef = "analyze",
            InputContractType = "Input",
            OutputContractType = "Output",
            ContextRequest = CreateContextRequest()
        };

        Assert.NotNull(node);
        Assert.Null(node.ArtifactKey);
        Assert.Null(node.Retry);
    }

    [Fact]
    public void AgentTaskNode_DeserializedFromJsonOmittingToolRefs_DefaultsToEmpty()
    {
        const string json = """
            {
                "node_id": "agent-1",
                "role": "analyst",
                "instructions_ref": "analyze",
                "input_contract_type": "Input",
                "output_contract_type": "Output",
                "context_request": {
                    "engagement_id": "eng-1",
                    "agent_role": "analyst",
                    "baseline_components": [],
                    "dynamic_fields": []
                }
            }
            """;

        var node = JsonSerializer.Deserialize<AgentTaskNode>(json, CanonicalProfile.Options);

        Assert.NotNull(node);
        Assert.Empty(node.ToolRefs);
    }

    #endregion

    #region HumanGateNode Tests

    [Fact]
    public void HumanGateNode_WithAllProperties_IsConstructible()
    {
        var node = new HumanGateNode
        {
            NodeId = "gate-1",
            GateKind = GateKind.Business,
            ApproverRoles = new[] { "director", "partner" },
            PromptTemplate = "Do you approve?",
            TimeoutMinutes = 60,
            RollbackToNodeId = "agent-1",
            ReapproveOnCascade = true,
            ArtifactKey = "approach"
        };

        Assert.Equal("gate-1", node.NodeId);
        Assert.Equal(GateKind.Business, node.GateKind);
        Assert.Equal(2, node.ApproverRoles.Count);
        Assert.Equal("Do you approve?", node.PromptTemplate);
        Assert.Equal(60, node.TimeoutMinutes);
        Assert.Equal("agent-1", node.RollbackToNodeId);
        Assert.True(node.ReapproveOnCascade);
    }

    [Fact]
    public void HumanGateNode_NodeTypeProperty_ReturnsHumanGate()
    {
        var node = new HumanGateNode
        {
            NodeId = "gate-1",
            GateKind = GateKind.Intake,
            ApproverRoles = new[] { "approver" },
            PromptTemplate = "Approve?",
            TimeoutMinutes = 30
        };

        Assert.Equal(NodeType.HumanGate, node.NodeType);
    }

    [Fact]
    public void HumanGateNode_WithNullRollback_IsConstructible()
    {
        var node = new HumanGateNode
        {
            NodeId = "gate-1",
            GateKind = GateKind.Technical,
            ApproverRoles = new[] { "engineer" },
            PromptTemplate = "Looks good?",
            TimeoutMinutes = 0,
            RollbackToNodeId = null
        };

        Assert.Null(node.RollbackToNodeId);
        Assert.Equal(0, node.TimeoutMinutes);
    }

    [Fact]
    public void HumanGateNode_ReapproveOnCascade_DefaultsToTrue()
    {
        var node = new HumanGateNode
        {
            NodeId = "gate-1",
            GateKind = GateKind.Business,
            ApproverRoles = new[] { "approver" },
            PromptTemplate = "Approve?",
            TimeoutMinutes = 30
        };

        Assert.True(node.ReapproveOnCascade);
    }

    [Fact]
    public void HumanGateNode_WithMultipleApproverRoles_IsConstructible()
    {
        var roles = new[] { "director", "partner", "principal", "qa-lead" };
        var node = new HumanGateNode
        {
            NodeId = "gate-1",
            GateKind = GateKind.Business,
            ApproverRoles = roles,
            PromptTemplate = "Approve?",
            TimeoutMinutes = 120
        };

        Assert.Equal(4, node.ApproverRoles.Count);
    }

    [Fact]
    public void HumanGateNode_Equality_ConsidersAllProperties()
    {
        var approverRoles = new[] { "director" };
        var node1 = new HumanGateNode
        {
            NodeId = "gate-1",
            GateKind = GateKind.Business,
            ApproverRoles = approverRoles,
            PromptTemplate = "Approve?",
            TimeoutMinutes = 60,
            ReapproveOnCascade = false
        };
        var node2 = new HumanGateNode
        {
            NodeId = "gate-1",
            GateKind = GateKind.Business,
            ApproverRoles = approverRoles,
            PromptTemplate = "Approve?",
            TimeoutMinutes = 60,
            ReapproveOnCascade = false
        };

        Assert.Equal(node1, node2);
    }

    #endregion

    #region DecisionNode Tests
#pragma warning disable CS0618 // Legacy string-predicate wire shape stays covered until the phase boundary removes it (S13.7j).

    [Fact]
    public void DecisionNode_WithAllProperties_IsConstructible()
    {
        var node = new DecisionNode
        {
            NodeId = "decision-1",
            Predicate = "output.score > 0.8",
            DefaultBranchNodeId = "fallback-node",
            ArtifactKey = "decision"
        };

        Assert.Equal("decision-1", node.NodeId);
        Assert.Equal("output.score > 0.8", node.Predicate);
        Assert.Equal("fallback-node", node.DefaultBranchNodeId);
        Assert.Equal("decision", node.ArtifactKey);
    }

    [Fact]
    public void DecisionNode_NodeTypeProperty_ReturnsDecision()
    {
        var node = new DecisionNode
        {
            NodeId = "decision-1",
            Predicate = "score > 0.8",
            DefaultBranchNodeId = "default"
        };

        Assert.Equal(NodeType.Decision, node.NodeType);
    }

    [Fact]
    public void DecisionNode_WithComplexPredicate_IsConstructible()
    {
        var node = new DecisionNode
        {
            NodeId = "decision-1",
            Predicate = "output.risk_level == 'high' AND output.confidence < 0.5",
            DefaultBranchNodeId = "escalate-node"
        };

        Assert.Equal("output.risk_level == 'high' AND output.confidence < 0.5", node.Predicate);
    }

    [Fact]
    public void DecisionNode_Equality_ConsidersAllProperties()
    {
        var node1 = new DecisionNode
        {
            NodeId = "decision-1",
            Predicate = "score > 0.8",
            DefaultBranchNodeId = "default"
        };
        var node2 = new DecisionNode
        {
            NodeId = "decision-1",
            Predicate = "score > 0.8",
            DefaultBranchNodeId = "default"
        };

        Assert.Equal(node1, node2);
    }

#pragma warning restore CS0618
    #endregion

    #region ParallelNode Tests

    [Fact]
    public void ParallelNode_WithAllProperties_IsConstructible()
    {
        var branches = new[] { "branch-1", "branch-2", "branch-3" };
        var node = new ParallelNode
        {
            NodeId = "parallel-1",
            BranchNodeIds = branches,
            JoinNodeId = "join-node",
            ArtifactKey = "parallel-work"
        };

        Assert.Equal("parallel-1", node.NodeId);
        Assert.Equal(3, node.BranchNodeIds.Count);
        Assert.Equal("join-node", node.JoinNodeId);
        Assert.Equal("parallel-work", node.ArtifactKey);
    }

    [Fact]
    public void ParallelNode_NodeTypeProperty_ReturnsParallel()
    {
        var node = new ParallelNode
        {
            NodeId = "parallel-1",
            BranchNodeIds = new[] { "b1", "b2" },
            JoinNodeId = "join"
        };

        Assert.Equal(NodeType.Parallel, node.NodeType);
    }

    [Fact]
    public void ParallelNode_WithTwoBranches_IsConstructible()
    {
        var node = new ParallelNode
        {
            NodeId = "parallel-1",
            BranchNodeIds = new[] { "left-branch", "right-branch" },
            JoinNodeId = "rejoin-point"
        };

        Assert.Equal(2, node.BranchNodeIds.Count);
    }

    [Fact]
    public void ParallelNode_WithManyBranches_IsConstructible()
    {
        var node = new ParallelNode
        {
            NodeId = "parallel-1",
            BranchNodeIds = new[] { "b1", "b2", "b3", "b4", "b5" },
            JoinNodeId = "join"
        };

        Assert.Equal(5, node.BranchNodeIds.Count);
    }

    [Fact]
    public void ParallelNode_Equality_ConsidersAllProperties()
    {
        var branches = new[] { "b1", "b2" };
        var node1 = new ParallelNode
        {
            NodeId = "parallel-1",
            BranchNodeIds = branches,
            JoinNodeId = "join"
        };
        var node2 = new ParallelNode
        {
            NodeId = "parallel-1",
            BranchNodeIds = branches,
            JoinNodeId = "join"
        };

        Assert.Equal(node1, node2);
    }

    #endregion

    #region LoopNode Tests

    [Fact]
    public void LoopNode_WithAllProperties_IsConstructible()
    {
        var node = new LoopNode
        {
            NodeId = "loop-1",
            BodyNodeId = "loop-body",
            MaxIterations = 10,
            ArtifactKey = "iteration"
        };

        Assert.Equal("loop-1", node.NodeId);
        Assert.Equal("loop-body", node.BodyNodeId);
        Assert.Equal(10, node.MaxIterations);
        Assert.Equal("iteration", node.ArtifactKey);
    }

    [Fact]
    public void LoopNode_NodeTypeProperty_ReturnsLoop()
    {
        var node = new LoopNode
        {
            NodeId = "loop-1",
            BodyNodeId = "body",
            MaxIterations = 5
        };

        Assert.Equal(NodeType.Loop, node.NodeType);
    }

    [Fact]
    public void LoopNode_WithZeroMaxIterations_IsConstructible()
    {
        var node = new LoopNode
        {
            NodeId = "loop-1",
            BodyNodeId = "body",
            MaxIterations = 0
        };

        Assert.Equal(0, node.MaxIterations);
    }

    [Fact]
    public void LoopNode_WithHighMaxIterations_IsConstructible()
    {
        var node = new LoopNode
        {
            NodeId = "loop-1",
            BodyNodeId = "body",
            MaxIterations = 1000
        };

        Assert.Equal(1000, node.MaxIterations);
    }

    [Fact]
    public void LoopNode_Equality_ConsidersAllProperties()
    {
        var node1 = new LoopNode
        {
            NodeId = "loop-1",
            BodyNodeId = "body",
            MaxIterations = 5
        };
        var node2 = new LoopNode
        {
            NodeId = "loop-1",
            BodyNodeId = "body",
            MaxIterations = 5
        };

        Assert.Equal(node1, node2);
    }

    #endregion

    #region McpToolNode Tests

    [Fact]
    public void McpToolNode_WithAllProperties_IsConstructible()
    {
        var node = new McpToolNode
        {
            NodeId = "mcp-1",
            ToolRef = "web-search",
            TimeoutSeconds = 30,
            IdempotencyKeySpec = "request_id",
            ArtifactKey = "research"
        };

        Assert.Equal("mcp-1", node.NodeId);
        Assert.Equal("web-search", node.ToolRef);
        Assert.Equal(30, node.TimeoutSeconds);
        Assert.Equal("request_id", node.IdempotencyKeySpec);
    }

    [Fact]
    public void McpToolNode_NodeTypeProperty_ReturnsMcpTool()
    {
        var node = new McpToolNode
        {
            NodeId = "mcp-1",
            ToolRef = "search",
            TimeoutSeconds = 20,
            IdempotencyKeySpec = "req_id"
        };

        Assert.Equal(NodeType.McpTool, node.NodeType);
    }

    [Fact]
    public void McpToolNode_WithRetryPolicy_IsConstructible()
    {
        var retry = new RetryPolicySpec { ProfileName = "external-api", MaxAttemptsOverride = 5 };
        var node = new McpToolNode
        {
            NodeId = "mcp-1",
            ToolRef = "api-call",
            TimeoutSeconds = 60,
            IdempotencyKeySpec = "tx_id",
            Retry = retry
        };

        Assert.NotNull(node.Retry);
        Assert.Equal("external-api", node.Retry.ProfileName);
    }

    [Fact]
    public void McpToolNode_WithVariousTimeouts_IsConstructible()
    {
        var node1 = new McpToolNode
        {
            NodeId = "mcp-1",
            ToolRef = "quick",
            TimeoutSeconds = 5,
            IdempotencyKeySpec = "id"
        };
        var node2 = new McpToolNode
        {
            NodeId = "mcp-2",
            ToolRef = "slow",
            TimeoutSeconds = 300,
            IdempotencyKeySpec = "id"
        };

        Assert.Equal(5, node1.TimeoutSeconds);
        Assert.Equal(300, node2.TimeoutSeconds);
    }

    [Fact]
    public void McpToolNode_Equality_ConsidersAllProperties()
    {
        var node1 = new McpToolNode
        {
            NodeId = "mcp-1",
            ToolRef = "search",
            TimeoutSeconds = 20,
            IdempotencyKeySpec = "req_id"
        };
        var node2 = new McpToolNode
        {
            NodeId = "mcp-1",
            ToolRef = "search",
            TimeoutSeconds = 20,
            IdempotencyKeySpec = "req_id"
        };

        Assert.Equal(node1, node2);
    }

    #endregion

    #region CascadeCheckNode Tests

    [Fact]
    public void CascadeCheckNode_WithAllProperties_IsConstructible()
    {
        var triggerArtifacts = new[] { "scope", "approach" };
        var node = new CascadeCheckNode
        {
            NodeId = "cascade-1",
            TriggerArtifactKeys = triggerArtifacts
        };

        Assert.Equal("cascade-1", node.NodeId);
        Assert.Equal(2, node.TriggerArtifactKeys.Count);
    }

    [Fact]
    public void CascadeCheckNode_NodeTypeProperty_ReturnsCascadeCheck()
    {
        var node = new CascadeCheckNode
        {
            NodeId = "cascade-1",
            TriggerArtifactKeys = new[] { "scope" }
        };

        Assert.Equal(NodeType.CascadeCheck, node.NodeType);
    }

    [Fact]
    public void CascadeCheckNode_WithMultipleTriggerArtifacts_IsConstructible()
    {
        var node = new CascadeCheckNode
        {
            NodeId = "cascade-1",
            TriggerArtifactKeys = new[] { "scope", "approach", "pricing", "timeline" }
        };

        Assert.Equal(4, node.TriggerArtifactKeys.Count);
    }

    [Fact]
    public void CascadeCheckNode_WithSingleTriggerArtifact_IsConstructible()
    {
        var node = new CascadeCheckNode
        {
            NodeId = "cascade-1",
            TriggerArtifactKeys = new[] { "scope" }
        };

        Assert.Single(node.TriggerArtifactKeys);
    }

    [Fact]
    public void CascadeCheckNode_Equality_ConsidersAllProperties()
    {
        var triggerArtifacts = new[] { "scope", "approach" };
        var node1 = new CascadeCheckNode
        {
            NodeId = "cascade-1",
            TriggerArtifactKeys = triggerArtifacts
        };
        var node2 = new CascadeCheckNode
        {
            NodeId = "cascade-1",
            TriggerArtifactKeys = triggerArtifacts
        };

        Assert.Equal(node1, node2);
    }

    #endregion

    #region Polymorphic Node Discrimination Tests

    [Fact]
    public void AllNodeTypes_ImplementWorkflowNode()
    {
        var nodes = new WorkflowNode[]
        {
            CreateAgentTaskNode(),
            CreateHumanGateNode(),
            CreateDecisionNode(),
            CreateParallelNode(),
            CreateLoopNode(),
            CreateMcpToolNode(),
            CreateCascadeCheckNode()
        };

        foreach (var node in nodes)
        {
            Assert.IsAssignableFrom<WorkflowNode>(node);
        }
    }

    [Fact]
    public void AllNodeTypes_HaveDistinctNodeTypeValues()
    {
        var nodeTypes = new[]
        {
            CreateAgentTaskNode().NodeType,
            CreateHumanGateNode().NodeType,
            CreateDecisionNode().NodeType,
            CreateParallelNode().NodeType,
            CreateLoopNode().NodeType,
            CreateMcpToolNode().NodeType,
            CreateCascadeCheckNode().NodeType
        };

        var uniqueTypes = nodeTypes.Distinct().ToList();
        Assert.Equal(nodeTypes.Length, uniqueTypes.Count);
    }

    [Fact]
    public void NodeWithoutArtifactKey_HasNullArtifactKey()
    {
        var node = CreateAgentTaskNode();
        Assert.Null(node.ArtifactKey);
    }

    [Fact]
    public void NodeWithArtifactKey_PreservesValue()
    {
        var node = new AgentTaskNode
        {
            NodeId = "agent-1",
            Role = "analyst",
            InstructionsRef = "analyze",
            InputContractType = "Input",
            OutputContractType = "Output",
            ContextRequest = CreateContextRequest(),
            ArtifactKey = "scope"
        };

        Assert.Equal("scope", node.ArtifactKey);
    }

    #endregion

    #region Helpers

    private static ContextRequest CreateContextRequest()
    {
        return new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = EmptyStringList,
            DynamicFields = EmptyStringList
        };
    }

    private static AgentTaskNode CreateAgentTaskNode()
    {
        return new AgentTaskNode
        {
            NodeId = "agent-1",
            Role = "analyst",
            InstructionsRef = "analyze",
            InputContractType = "Input",
            OutputContractType = "Output",
            ContextRequest = CreateContextRequest()
        };
    }

    private static HumanGateNode CreateHumanGateNode()
    {
        return new HumanGateNode
        {
            NodeId = "gate-1",
            GateKind = GateKind.Business,
            ApproverRoles = new[] { "director" },
            PromptTemplate = "Approve?",
            TimeoutMinutes = 60
        };
    }

    private static DecisionNode CreateDecisionNode()
    {
#pragma warning disable CS0618 // Legacy string-predicate wire shape stays covered until the phase boundary removes it (S13.7j).
        return new DecisionNode
        {
            NodeId = "decision-1",
            Predicate = "score > 0.8",
            DefaultBranchNodeId = "default"
        };
#pragma warning restore CS0618
    }

    private static ParallelNode CreateParallelNode()
    {
        return new ParallelNode
        {
            NodeId = "parallel-1",
            BranchNodeIds = new[] { "b1", "b2" },
            JoinNodeId = "join"
        };
    }

    private static LoopNode CreateLoopNode()
    {
        return new LoopNode
        {
            NodeId = "loop-1",
            BodyNodeId = "body",
            MaxIterations = 5
        };
    }

    private static McpToolNode CreateMcpToolNode()
    {
        return new McpToolNode
        {
            NodeId = "mcp-1",
            ToolRef = "search",
            TimeoutSeconds = 20,
            IdempotencyKeySpec = "req_id"
        };
    }

    private static CascadeCheckNode CreateCascadeCheckNode()
    {
        return new CascadeCheckNode
        {
            NodeId = "cascade-1",
            TriggerArtifactKeys = new[] { "scope" }
        };
    }

    #endregion
}
