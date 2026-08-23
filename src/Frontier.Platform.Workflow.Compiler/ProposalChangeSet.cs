using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// One reviewable change in an agent proposal (doc 14 §4.1). The <see cref="ChangeId"/> is the
/// stable token the UI checkboxes carry and the merge re-resolves — its vocabulary is
/// <c>{kind}:{action}:{ref}</c> (e.g. <c>node:added:business-gate-1</c>,
/// <c>edge:added:effort→business-gate-1</c>), produced solely by <see cref="ProposalChangeSetBuilder"/>
/// so the surface and merge sides cannot drift.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain DTO; values are exercised by the change-set builder and merge tests.")]
public sealed record ProposalChangeItem
{
    /// <summary>Stable id: <c>{kind}:{action}:{ref}</c>.</summary>
    [JsonPropertyName("changeId")]
    public required string ChangeId { get; init; }

    /// <summary><c>added</c> | <c>removed</c> | <c>modified</c>.</summary>
    [JsonPropertyName("changeType")]
    public required string ChangeType { get; init; }

    /// <summary>The affected node id (for node changes); <c>null</c> for edge changes.</summary>
    [JsonPropertyName("nodeId")]
    public string? NodeId { get; init; }

    /// <summary>
    /// The node's wire type name (doc 00 §3.2, e.g. <c>"agent_task"</c>), for node changes only —
    /// lets the diff card reuse the S9.36 <c>WorkflowNodeWidget</c> shape/icon per doc 19 §A3-R2
    /// ("same widgets reused in chat diff-card previews"). <c>null</c> for edge changes and for a
    /// <c>removed</c> node change where the type can't be recovered (see
    /// <see cref="ProposalChangeSetBuilder.Build"/>).
    /// </summary>
    [JsonPropertyName("nodeType")]
    public string? NodeType { get; init; }

    /// <summary>Human-readable summary for the diff card.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

/// <summary>
/// Builds the granular change set between two definitions from the structural diff (doc 14 §4.1).
/// The single source of the <c>changeId</c> vocabulary — invoked at turn time (to surface the diff)
/// and at merge time (to interpret approved ids), so the two sides agree by construction.
/// </summary>
// Public for the designer, which stays with the consumer until E3b step 5. A designer host
// builds the change set it shows the user from the same builder the merge and diff services
// use, so the two never disagree about what changed.
public static class ProposalChangeSetBuilder
{
    internal const string Node = "node";
    internal const string Edge = "edge";
    internal const string Added = "added";
    internal const string Removed = "removed";
    internal const string Modified = "modified";

    /// <summary>
    /// Produces the ordered change list for the diff between <paramref name="from"/> and
    /// <paramref name="to"/> (S9.33: node type is looked up from whichever side still has the
    /// node — <paramref name="to"/> for added/modified, <paramref name="from"/> for removed).
    /// </summary>
    internal static IReadOnlyList<ProposalChangeItem> Build(WorkflowDefinitionDiff diff, WorkflowDefinition from, WorkflowDefinition to)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        var fromById = from.Nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);
        var toById = to.Nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);

        var changes = new List<ProposalChangeItem>();
        changes.AddRange(diff.NodesAdded.Select(id => NodeChange(Added, id, toById.GetValueOrDefault(id)?.NodeType.Name)));
        changes.AddRange(diff.NodesRemoved.Select(id => NodeChange(Removed, id, fromById.GetValueOrDefault(id)?.NodeType.Name)));
        changes.AddRange(diff.NodesModified.Select(id => NodeChange(Modified, id, toById.GetValueOrDefault(id)?.NodeType.Name)));
        changes.AddRange(diff.EdgesAdded.Select(key => EdgeChange(Added, key)));
        changes.AddRange(diff.EdgesRemoved.Select(key => EdgeChange(Removed, key)));
        return changes;
    }

    /// <summary>Parses a <c>changeId</c> back into its parts; <c>false</c> if malformed.</summary>
    internal static bool TryParse(string changeId, out string kind, out string action, out string reference)
    {
        kind = action = reference = string.Empty;
        if (string.IsNullOrEmpty(changeId)) return false;

        var firstColon = changeId.IndexOf(':', StringComparison.Ordinal);
        if (firstColon < 0) return false;
        var secondColon = changeId.IndexOf(':', firstColon + 1);
        if (secondColon < 0) return false;

        kind = changeId[..firstColon];
        action = changeId[(firstColon + 1)..secondColon];
        reference = changeId[(secondColon + 1)..]; // edge refs contain "→" but no further ':'
        return reference.Length > 0;
    }

    /// <summary>
    /// The canonical edge key, matching the diff service: <c>{from}→{to} ({kind})</c>. The kind
    /// is part of an edge's identity — a control and a data edge legally coexist between the same
    /// node pair, and a kind-less key made one silently overwrite the other during merge
    /// (S9.27 walkthrough: the dropped control edges broke the single-entry rule on apply).
    /// Parenthesised, not another <c>:</c> segment, so <see cref="TryParse"/>'s
    /// "no further ':' in an edge ref" invariant holds.
    /// </summary>
    internal static string EdgeKey(WorkflowEdge edge) => $"{edge.FromNodeId}→{edge.ToNodeId} ({edge.Kind.Name})";

    /// <summary>Builds a node change id: <c>node:{action}:{nodeId}</c>.</summary>
    internal static string NodeChangeId(string action, string nodeId) => $"{Node}:{action}:{nodeId}";

    /// <summary>Builds an edge change id: <c>edge:{action}:{from}→{to}</c>.</summary>
    internal static string EdgeChangeId(string action, string edgeKey) => $"{Edge}:{action}:{edgeKey}";

    internal static ProposalChangeItem NodeChange(string action, string nodeId, string? nodeType) => new()
    {
        ChangeId = NodeChangeId(action, nodeId),
        ChangeType = action,
        NodeId = nodeId,
        NodeType = nodeType,
        Description = $"{Capitalize(action)} node '{nodeId}'",
    };

    internal static ProposalChangeItem EdgeChange(string action, string edgeKey) => new()
    {
        ChangeId = EdgeChangeId(action, edgeKey),
        ChangeType = action,
        NodeId = null,
        Description = $"{Capitalize(action)} edge {edgeKey}",
    };

    internal static string Capitalize(string action) => action switch
    {
        Added => "Add",
        Removed => "Remove",
        Modified => "Modify",
        _ => action,
    };
}
