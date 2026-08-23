using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Mirrors ArtifactState's <c>ArtifactStateActivityResult</c> wire shape (doc 02 §2-3) for
/// the <see cref="Abstractions.WorkflowActivityNames.ArtifactStateActivity"/> call from
/// <see cref="GraphOrchestratorSteps"/>. See <see cref="SnapshotActivityResponse"/> for why
/// this is duplicated rather than referenced (library-boundaries: subsystem libraries
/// don't reference each other).
/// </summary>
public sealed record ArtifactStateActivityResponse
{
    /// <summary>The <c>artifact-state</c> version document id written for this section version.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("section_ref")]
    public required string SectionRef { get; init; }
}
