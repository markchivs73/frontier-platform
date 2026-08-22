using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class WorkflowDefinitionTests
{
    [Fact]
    public void Validate_WellFormedDefinition_DoesNotThrow()
    {
        var definition = Definition(
            nodes: [Node("a"), Node("b")],
            edges: [Edge("a", "b")]);

        definition.Validate();
    }

    [Fact]
    public void Validate_DuplicateNodeIds_Throws()
    {
        var definition = Definition(
            nodes: [Node("a"), Node("a")],
            edges: []);

        var exception = Assert.Throws<ContractViolationException>(definition.Validate);

        Assert.Equal(nameof(WorkflowDefinition), exception.ContractType);
        Assert.Contains("Duplicate node id 'a'.", exception.Violations);
    }

    [Fact]
    public void Validate_AccumulatesViolationsAcrossAllChecks()
    {
        var definition = Definition(
            nodes: [Node("a"), Node("a")],
            edges: [Edge("a", "missing"), Edge("missing", "a")]);

        var exception = Assert.Throws<ContractViolationException>(definition.Validate);

        Assert.Contains("Duplicate node id 'a'.", exception.Violations);
        Assert.Contains("Edge references unknown to_node_id 'missing'.", exception.Violations);
        Assert.Contains("Edge references unknown from_node_id 'missing'.", exception.Violations);
    }

    static WorkflowDefinition Definition(IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<WorkflowEdge> edges) => new()
    {
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Test workflow",
        Nodes = nodes,
        Edges = edges,
        DefinitionHash = "deadbeef",
        Mode = ExecutionMode.OneShot,
    };

    static CascadeCheckNode Node(string nodeId) => new()
    {
        NodeId = nodeId,
        TriggerArtifactKeys = [],
    };

    static WorkflowEdge Edge(string fromNodeId, string toNodeId) => new()
    {
        FromNodeId = fromNodeId,
        ToNodeId = toNodeId,
        Kind = EdgeKind.Control,
    };
}
