using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Mirrors ArtifactState's <c>ArtifactVersionWrite</c> wire shape (doc 02 §2-3) for the
/// <see cref="Abstractions.WorkflowActivityNames.ArtifactStateActivity"/> call from
/// <see cref="GraphOrchestratorSteps"/>. See <see cref="SnapshotActivityResponse"/> for why
/// this is duplicated rather than referenced (library-boundaries: subsystem libraries
/// don't reference each other).
/// </summary>
public sealed record ArtifactStateActivityRequest
{
    /// <summary>The execution writing this section version.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>The partition key (doc 02 §3, ADR-S1).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The section this version belongs to.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("artifact_key")]
    public required string ArtifactKey { get; init; }

    /// <summary>This version's number within the section's history, minted by the orchestrator's per-section counter.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("version")]
    public required int Version { get; init; }

    /// <summary>The section's output payload at this version.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>SHA256 hex hash of <see cref="Content"/>'s canonical bytes.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }

    /// <summary>UTC timestamp at which this version was written.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("updated_at_utc")]
    public required DateTime UpdatedAtUtc { get; init; }
}
