
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Node diff service: computes structural differences between workflow definitions.
/// Used by merge logic (conflict detection) and UI (proposal visualization).
/// </summary>
public interface INodeDiffService
{
    /// <summary>
    /// Compute the diff between two definitions. Returns node-level changes (added, removed, modified).
    /// </summary>
    WorkflowDefinitionDiff Compute(
        WorkflowDefinition from,
        WorkflowDefinition target);
}

/// <summary>Structural diff between two workflow definitions.</summary>
public sealed record WorkflowDefinitionDiff
{
    public required IReadOnlyList<string> NodesAdded { get; init; }
    public required IReadOnlyList<string> NodesRemoved { get; init; }
    public required IReadOnlyList<string> NodesModified { get; init; }
    public required IReadOnlyList<string> EdgesAdded { get; init; }
    public required IReadOnlyList<string> EdgesRemoved { get; init; }
    public required IReadOnlyList<string> EdgesModified { get; init; }
    public bool HasChanges => NodesAdded.Count > 0 || NodesRemoved.Count > 0 || NodesModified.Count > 0
                            || EdgesAdded.Count > 0 || EdgesRemoved.Count > 0 || EdgesModified.Count > 0;
}
