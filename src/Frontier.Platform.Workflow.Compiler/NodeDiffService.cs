using Frontier.Platform.Abstractions;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Phase 1 node diff computation: structural changes between two workflow definitions.
/// Compares node IDs and edge (fromNodeId, toNodeId) pairs to detect added/removed/modified.
/// </summary>
public sealed class NodeDiffService : INodeDiffService
{
    public WorkflowDefinitionDiff Compute(
        WorkflowDefinition from,
        WorkflowDefinition target)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(target);

        var fromNodeIds = new HashSet<string>(from.Nodes.Select(n => n.NodeId));
        var targetNodeIds = new HashSet<string>(target.Nodes.Select(n => n.NodeId));

        // One shared key format with the merge path (ProposalChangeSetBuilder.EdgeKey) — the
        // kind is part of an edge's identity, or a control/data pair between the same nodes
        // collapses to a single diff entry and the merge drops one of them (S9.27).
        var fromEdgeKeys = from.Edges.Select(ProposalChangeSetBuilder.EdgeKey).ToHashSet();
        var targetEdgeKeys = target.Edges.Select(ProposalChangeSetBuilder.EdgeKey).ToHashSet();

        var nodesAdded = targetNodeIds.Except(fromNodeIds).ToList().AsReadOnly();
        var nodesRemoved = fromNodeIds.Except(targetNodeIds).ToList().AsReadOnly();

        // For "modified" nodes, detect those with structural differences
        var nodesModified = DetectModifiedNodes(from, target, fromNodeIds.Intersect(targetNodeIds)).AsReadOnly();

        var edgesAdded = targetEdgeKeys.Except(fromEdgeKeys).ToList().AsReadOnly();
        var edgesRemoved = fromEdgeKeys.Except(targetEdgeKeys).ToList().AsReadOnly();
        var edgesModified = new List<string>().AsReadOnly(); // Phase 1: edges are immutable once added

        return new WorkflowDefinitionDiff
        {
            NodesAdded = nodesAdded,
            NodesRemoved = nodesRemoved,
            NodesModified = nodesModified,
            EdgesAdded = edgesAdded,
            EdgesRemoved = edgesRemoved,
            EdgesModified = edgesModified
        };
    }

    private static List<string> DetectModifiedNodes(
        WorkflowDefinition from,
        WorkflowDefinition to,
        IEnumerable<string> commonNodeIds)
    {
        var modified = new List<string>();
        var fromNodeDict = from.Nodes.ToDictionary(n => n.NodeId);
        var toNodeDict = to.Nodes.ToDictionary(n => n.NodeId);

        foreach (var nodeId in commonNodeIds)
        {
            if (fromNodeDict.TryGetValue(nodeId, out var fromNode) &&
                toNodeDict.TryGetValue(nodeId, out var toNode))
            {
                // Phase 1: simple structural comparison (type, role/gate-kind, instructions, section)
                // A full implementation would do byte-level comparison, but for PoC, node-type equivalence suffices
                if (fromNode.NodeType != toNode.NodeType ||
                    GetNodeKey(fromNode) != GetNodeKey(toNode))
                {
                    modified.Add(nodeId);
                }
            }
        }

        return modified;
    }

    private static string GetNodeKey(WorkflowNode node)
    {
        // Phase 1: extract key fields that indicate a meaningful change
        return node switch
        {
            AgentTaskNode agentNode => $"{agentNode.NodeType}:{agentNode.Role}:{agentNode.InputContractType}",
            HumanGateNode gateNode => $"{gateNode.NodeType}:{gateNode.GateKind}:{gateNode.TimeoutMinutes}",
#pragma warning disable CS0618 // Legacy predicate stays in the diff key so pre-S13.7j definitions still diff correctly.
            DecisionNode decisionNode => $"{decisionNode.NodeType}:{decisionNode.Predicate}:{decisionNode.DefaultBranchNodeId}:{BranchesKey(decisionNode)}",
#pragma warning restore CS0618
            ParallelNode parallelNode => $"{parallelNode.NodeType}",
            LoopNode loopNode => $"{loopNode.NodeType}:{loopNode.MaxIterations}",
            McpToolNode toolNode => $"{toolNode.NodeType}:{toolNode.ToolRef}",
            CascadeCheckNode cascadeNode => $"{cascadeNode.NodeType}",
            _ => node.NodeType.ToString()
        };
    }

    /// <summary>Canonical-bytes key for a decision's branch tree (S13.7j) — a branches edit must register as a node change.</summary>
    internal static string BranchesKey(DecisionNode node) =>
        node.Branches is null ? "-" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(node.Branches, Frontier.Platform.Serialization.CanonicalProfile.Options)));
}
