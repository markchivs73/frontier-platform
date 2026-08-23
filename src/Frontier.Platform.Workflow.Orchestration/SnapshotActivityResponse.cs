using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Mirrors ArtifactState's <c>SnapshotStateActivityResult</c> wire shape (doc 02 §5) for
/// the <see cref="Abstractions.WorkflowActivityNames.SnapshotStateActivity"/> call from
/// <see cref="GraphOrchestratorSteps"/>. See <see cref="CascadeActivityResponse"/> for why
/// this is duplicated rather than referenced (library-boundaries: subsystem libraries
/// don't reference each other).
/// </summary>
public sealed record SnapshotActivityResponse
{
    /// <summary>The <c>execution-snapshots</c> document id written for this checkpoint.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("snapshot_id")]
    public required string SnapshotId { get; init; }
}
