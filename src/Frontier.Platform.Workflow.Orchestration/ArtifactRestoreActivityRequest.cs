using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Mirrors ArtifactState's <c>ArtifactRestoreRequest</c> wire shape (doc 06 §6) for the
/// <see cref="Abstractions.WorkflowActivityNames.RestoreArtifactsActivity"/> call from
/// <see cref="GraphOrchestratorSteps"/>. See <see cref="ArtifactStateActivityResponse"/> for
/// why this is duplicated rather than referenced (library-boundaries: subsystem libraries
/// don't reference each other).
/// </summary>
public sealed record ArtifactRestoreActivityRequest
{
    /// <summary>The partition key (doc 02 §3, ADR-S1).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The approved <c>artifact-state</c> version document id to repoint <c>current</c> at (from <c>ExecutionSnapshot.ApprovedSnapshotRefs</c>).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("restore_ref")]
    public required string RestoreRef { get; init; }

    /// <summary>UTC timestamp at which the restore was performed.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("restored_at_utc")]
    public required DateTime RestoredAtUtc { get; init; }
}
