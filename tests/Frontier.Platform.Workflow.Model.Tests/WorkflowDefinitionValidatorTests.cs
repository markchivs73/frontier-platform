namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class WorkflowDefinitionValidatorTests
{
    [Fact]
    public void ValidateUniqueNodeIds_NoDuplicates_ReturnsNoViolations()
    {
        var nodes = new[] { Node("a"), Node("b") };

        var violations = WorkflowDefinitionValidator.ValidateUniqueNodeIds(nodes);

        Assert.Empty(violations);
    }

    [Fact]
    public void ValidateUniqueNodeIds_DuplicateIds_ReturnsViolation()
    {
        var nodes = new[] { Node("a"), Node("a") };

        var violations = WorkflowDefinitionValidator.ValidateUniqueNodeIds(nodes);

        Assert.Equal(["Duplicate node id 'a'."], violations);
    }

    [Fact]
    public void ValidateEdgesResolve_AllNodeIdsKnown_ReturnsNoViolations()
    {
        var nodes = new[] { Node("a"), Node("b") };
        var edges = new[] { Edge("a", "b") };

        var violations = WorkflowDefinitionValidator.ValidateEdgesResolve(nodes, edges);

        Assert.Empty(violations);
    }

    [Fact]
    public void ValidateEdgesResolve_UnknownFromNodeId_ReturnsViolation()
    {
        var nodes = new[] { Node("a"), Node("b") };
        var edges = new[] { Edge("missing", "b") };

        var violations = WorkflowDefinitionValidator.ValidateEdgesResolve(nodes, edges);

        Assert.Equal(["Edge references unknown from_node_id 'missing'."], violations);
    }

    [Fact]
    public void ValidateEdgesResolve_UnknownToNodeId_ReturnsViolation()
    {
        var nodes = new[] { Node("a"), Node("b") };
        var edges = new[] { Edge("a", "missing") };

        var violations = WorkflowDefinitionValidator.ValidateEdgesResolve(nodes, edges);

        Assert.Equal(["Edge references unknown to_node_id 'missing'."], violations);
    }

    [Fact]
    public void ValidateAcyclic_DiamondShapedGraph_ReturnsNoViolations()
    {
        var nodes = new[] { Node("a"), Node("b"), Node("c"), Node("d") };
        var edges = new[] { Edge("a", "b"), Edge("a", "c"), Edge("b", "d"), Edge("c", "d") };

        var violations = WorkflowDefinitionValidator.ValidateAcyclic(nodes, edges);

        Assert.Empty(violations);
    }

    [Fact]
    public void ValidateAcyclic_CyclicGraph_ReturnsViolation()
    {
        var nodes = new[] { Node("a"), Node("b"), Node("c") };
        var edges = new[] { Edge("a", "b"), Edge("b", "c"), Edge("c", "a") };

        var violations = WorkflowDefinitionValidator.ValidateAcyclic(nodes, edges);

        Assert.Equal(["Control-edge graph contains a cycle."], violations);
    }

    [Fact]
    public void ValidateAcyclic_DataEdgeFormsCycle_IsIgnored()
    {
        var nodes = new[] { Node("a"), Node("b") };
        var edges = new[] { Edge("a", "b"), Edge("b", "a", EdgeKind.Data) };

        var violations = WorkflowDefinitionValidator.ValidateAcyclic(nodes, edges);

        Assert.Empty(violations);
    }

    static CascadeCheckNode Node(string nodeId) => new()
    {
        NodeId = nodeId,
        TriggerArtifactKeys = [],
    };

    static WorkflowEdge Edge(string fromNodeId, string toNodeId, EdgeKind? kind = null) => new()
    {
        FromNodeId = fromNodeId,
        ToNodeId = toNodeId,
        Kind = kind ?? EdgeKind.Control,
    };
}
