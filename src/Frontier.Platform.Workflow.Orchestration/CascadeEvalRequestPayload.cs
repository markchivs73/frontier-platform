using System.Text.Json.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Mirrors CascadeLogic's <c>CascadeEvalRequest</c> wire shape (doc 03 §3) for the
/// <see cref="WorkflowActivityNames.EvaluateCascadeActivity"/> call from
/// <see cref="GraphOrchestratorSteps"/>. Deliberately duplicated rather than referenced
/// (library-boundaries: Orchestration and CascadeLogic are sibling subsystem libraries
/// and may not reference each other) — the shared <see cref="WorkflowActivityNames"/>
/// constant keeps the activity name in agreement; the JSON wire shape keeps the payload
/// in agreement.
/// </summary>
public sealed record CascadeEvalRequestPayload
{
    /// <summary>The execution whose section statuses are supplied in <see cref="CurrentArtifactStatuses"/>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>The section that was updated, triggering this cascade evaluation.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("changed_section")]
    public required string ChangedArtifact { get; init; }

    /// <summary>The execution's current status for every section it tracks.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("current_section_statuses")]
    public required IReadOnlyDictionary<string, ArtifactStatus> CurrentArtifactStatuses { get; init; }
}
