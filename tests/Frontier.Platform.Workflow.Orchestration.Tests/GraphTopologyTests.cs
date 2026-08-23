using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S2.2 tests for <see cref="GraphTopology"/>'s Kahn's-algorithm execution order.</summary>
public sealed class GraphTopologyTests
{
    [Fact]
    public void ExecutionOrder_ThreeArtifactChain_ReturnsTopologicalOrder()
    {
        var definition = OrchestrationFixtures.ThreeArtifactChain();

        var order = GraphTopology.ExecutionOrder(definition);

        Assert.Equal(["scope-agent", "approach-agent", "pricing-agent"], order);
    }

    [Fact]
    public void ExecutionOrder_CyclicGraph_ThrowsContractViolationException()
    {
        var definition = OrchestrationFixtures.TwoNodeCycle();

        var exception = Assert.Throws<ContractViolationException>(() => GraphTopology.ExecutionOrder(definition));

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAdjacency_ReturnsSortedSuccessorsPerNode()
    {
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var nodeIds = definition.Nodes.Select(node => node.NodeId).ToList();

        var adjacency = GraphTopology.BuildAdjacency(definition.Edges, nodeIds);

        Assert.Equal(["approach-agent"], adjacency["scope-agent"]);
        Assert.Equal(["pricing-agent"], adjacency["approach-agent"]);
        Assert.Empty(adjacency["pricing-agent"]);
    }

    [Fact]
    public void ComputeInDegrees_CountsDirectUpstreamEdges()
    {
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var nodeIds = definition.Nodes.Select(node => node.NodeId).ToList();
        var adjacency = GraphTopology.BuildAdjacency(definition.Edges, nodeIds);

        var inDegree = GraphTopology.ComputeInDegrees(adjacency, nodeIds);

        Assert.Equal(0, inDegree["scope-agent"]);
        Assert.Equal(1, inDegree["approach-agent"]);
        Assert.Equal(1, inDegree["pricing-agent"]);
    }

    [Fact]
    public void ReleaseSuccessors_DecrementsInDegree_AndQueuesNewlyReadyNodes()
    {
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var nodeIds = definition.Nodes.Select(node => node.NodeId).ToList();
        var adjacency = GraphTopology.BuildAdjacency(definition.Edges, nodeIds);
        var inDegree = GraphTopology.ComputeInDegrees(adjacency, nodeIds);
        var ready = new SortedSet<string>(StringComparer.Ordinal);

        GraphTopology.ReleaseSuccessors(adjacency, inDegree, ready, "scope-agent");

        Assert.Equal(0, inDegree["approach-agent"]);
        Assert.Contains("approach-agent", ready);
    }
}
