using Frontier.Platform.Abstractions;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Phase 1 node diff computation: structural changes between two workflow definitions.
/// Compares node IDs, each shared node's canonical bytes, and edge keys to detect added/removed/modified.
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
        var fromNodeDict = from.Nodes.ToDictionary(n => n.NodeId);
        var toNodeDict = to.Nodes.ToDictionary(n => n.NodeId);

        // GOV-02: every node field takes part — a key over a few fields let edits to the rest
        // (instructions, contracts, tools, retry, timeouts, writes…) produce no change item.
        return commonNodeIds
            .Where(id => NodeKey(fromNodeDict[id]) != NodeKey(toNodeDict[id]))
            .ToList();
    }

    /// <summary>
    /// Canonical-bytes key for a whole node, discriminator included — any field edit, or a type
    /// change under the same id, registers as a node change (doc 14 §4.1; GOV-02).
    /// </summary>
    internal static string NodeKey(WorkflowNode node) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(node, Frontier.Platform.Serialization.CanonicalProfile.Options)));
}
