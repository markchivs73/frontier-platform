using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

#pragma warning disable CA1861
public sealed class NodeDiffServiceTests
{
    private static readonly IReadOnlyList<string> EmptyNodes = Array.Empty<string>();

    private readonly NodeDiffService _service = new();

    [Fact]
    public void Compute_IdenticalDefinitions_NoChanges()
    {
        var def = CreateMinimalDefinition("wf-1");

        var diff = _service.Compute(def, def);

        Assert.Empty(diff.NodesAdded);
        Assert.Empty(diff.NodesRemoved);
        Assert.Empty(diff.NodesModified);
        Assert.Empty(diff.EdgesAdded);
        Assert.Empty(diff.EdgesRemoved);
        Assert.False(diff.HasChanges);
    }

    [Fact]
    public void Compute_NodeAdded_ReportsAsAdded()
    {
        var from = CreateDefinitionWithNodes("wf-1", new[] { "node-1" });
        var to = CreateDefinitionWithNodes("wf-1", new[] { "node-1", "node-2" });

        var diff = _service.Compute(from, to);

        Assert.Single(diff.NodesAdded);
        Assert.Contains("node-2", diff.NodesAdded);
        Assert.Empty(diff.NodesRemoved);
        Assert.Empty(diff.NodesModified);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Compute_NodeRemoved_ReportsAsRemoved()
    {
        var from = CreateDefinitionWithNodes("wf-1", new[] { "node-1", "node-2" });
        var to = CreateDefinitionWithNodes("wf-1", new[] { "node-1" });

        var diff = _service.Compute(from, to);

        Assert.Empty(diff.NodesAdded);
        Assert.Single(diff.NodesRemoved);
        Assert.Contains("node-2", diff.NodesRemoved);
        Assert.Empty(diff.NodesModified);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Compute_NodeModified_ReportsAsModified()
    {
        var from = CreateDefinitionWithNodeRoles("wf-1", new[] { ("node-1", "role-1") });
        var to = CreateDefinitionWithNodeRoles("wf-1", new[] { ("node-1", "role-2") });

        var diff = _service.Compute(from, to);

        Assert.Empty(diff.NodesAdded);
        Assert.Empty(diff.NodesRemoved);
        Assert.Single(diff.NodesModified);
        Assert.Contains("node-1", diff.NodesModified);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Compute_EdgeAdded_ReportsAsAdded()
    {
        var from = CreateDefinitionWithNodes("wf-1", new[] { "node-1", "node-2" });
        var to = CreateDefinitionWithNodesAndEdges("wf-1", new[] { "node-1", "node-2" },
            new[] { new WorkflowEdge { FromNodeId = "node-1", ToNodeId = "node-2", Kind = EdgeKind.Control } });

        var diff = _service.Compute(from, to);

        Assert.Empty(diff.NodesAdded);
        Assert.Single(diff.EdgesAdded);
        Assert.Contains("node-1→node-2 (control)", diff.EdgesAdded);
        Assert.Empty(diff.EdgesRemoved);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Compute_EdgeRemoved_ReportsAsRemoved()
    {
        var from = CreateDefinitionWithNodesAndEdges("wf-1", new[] { "node-1", "node-2" },
            new[] { new WorkflowEdge { FromNodeId = "node-1", ToNodeId = "node-2", Kind = EdgeKind.Control } });
        var to = CreateDefinitionWithNodes("wf-1", new[] { "node-1", "node-2" });

        var diff = _service.Compute(from, to);

        Assert.Empty(diff.EdgesAdded);
        Assert.Single(diff.EdgesRemoved);
        Assert.Contains("node-1→node-2 (control)", diff.EdgesRemoved);
        Assert.Empty(diff.EdgesModified);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Compute_MultipleChanges_ReportsAll()
    {
        var from = CreateDefinitionWithNodes("wf-1", new[] { "node-1", "node-2", "node-3" });
        var to = CreateDefinitionWithNodes("wf-1", new[] { "node-1", "node-2", "node-4" });

        var diff = _service.Compute(from, to);

        Assert.Single(diff.NodesAdded);
        Assert.Contains("node-4", diff.NodesAdded);
        Assert.Single(diff.NodesRemoved);
        Assert.Contains("node-3", diff.NodesRemoved);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Compute_NullFrom_Throws()
    {
        var to = CreateMinimalDefinition("wf-1");

        Assert.Throws<ArgumentNullException>(() => _service.Compute(null!, to));
    }

    [Fact]
    public void Compute_NullTarget_Throws()
    {
        var from = CreateMinimalDefinition("wf-1");

        Assert.Throws<ArgumentNullException>(() => _service.Compute(from, null!));
    }

    [Fact]
    public void Compute_AgentNodeRoleChange_Detected()
    {
        var from = CreateDefinitionWithNodeRoles("wf-1", new[] { ("agent-1", "role-a") });
        var to = CreateDefinitionWithNodeRoles("wf-1", new[] { ("agent-1", "role-b") });

        var diff = _service.Compute(from, to);

        Assert.Single(diff.NodesModified);
        Assert.Contains("agent-1", diff.NodesModified);
    }

    private static WorkflowDefinition CreateMinimalDefinition(string workflowId)
    {
        return CreateDefinitionWithNodes(workflowId, new[] { "node-1" });
    }

    private static WorkflowDefinition CreateDefinitionWithNodes(string workflowId, string[] nodeIds)
    {
        var nodes = nodeIds.Select(id => new AgentTaskNode
        {
            NodeId = id,
            Role = "default-role",
            InstructionsRef = "default-instructions",
            InputContractType = "DefaultInput",
            OutputContractType = "DefaultOutput",
            ContextRequest = new ContextRequest
            {
                EngagementId = "eng-1",
                AgentRole = "default-role",
                BaselineComponents = EmptyNodes,
                DynamicFields = EmptyNodes
            }
        }).Cast<WorkflowNode>().ToList();

        return new WorkflowDefinition
        {
            WorkflowId = workflowId,
            DefinitionVersion = 1,
            EngagementType = "test-type",
            Name = "Test Workflow",
            Nodes = nodes,
            Edges = new List<WorkflowEdge>(),
            DefinitionHash = "test-hash",
            Mode = ExecutionMode.OneShot
        };
    }

    private static WorkflowDefinition CreateDefinitionWithNodeRoles(
        string workflowId,
        (string id, string role)[] nodeDetails)
    {
        var nodes = nodeDetails.Select(nd => new AgentTaskNode
        {
            NodeId = nd.id,
            Role = nd.role,
            InstructionsRef = "default-instructions",
            InputContractType = "DefaultInput",
            OutputContractType = "DefaultOutput",
            ContextRequest = new ContextRequest
            {
                EngagementId = "eng-1",
                AgentRole = nd.role,
                BaselineComponents = EmptyNodes,
                DynamicFields = EmptyNodes
            }
        }).Cast<WorkflowNode>().ToList();

        return new WorkflowDefinition
        {
            WorkflowId = workflowId,
            DefinitionVersion = 1,
            EngagementType = "test-type",
            Name = "Test Workflow",
            Nodes = nodes,
            Edges = new List<WorkflowEdge>(),
            DefinitionHash = "test-hash",
            Mode = ExecutionMode.OneShot
        };
    }

    private static WorkflowDefinition CreateDefinitionWithNodesAndEdges(
        string workflowId,
        string[] nodeIds,
        WorkflowEdge[] edges)
    {
        var nodes = nodeIds.Select(id => new AgentTaskNode
        {
            NodeId = id,
            Role = "default-role",
            InstructionsRef = "default-instructions",
            InputContractType = "DefaultInput",
            OutputContractType = "DefaultOutput",
            ContextRequest = new ContextRequest
            {
                EngagementId = "eng-1",
                AgentRole = "default-role",
                BaselineComponents = EmptyNodes,
                DynamicFields = EmptyNodes
            }
        }).Cast<WorkflowNode>().ToList();

        return new WorkflowDefinition
        {
            WorkflowId = workflowId,
            DefinitionVersion = 1,
            EngagementType = "test-type",
            Name = "Test Workflow",
            Nodes = nodes,
            Edges = edges.ToList(),
            DefinitionHash = "test-hash",
            Mode = ExecutionMode.OneShot
        };
    }
}
