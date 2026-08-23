using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Mirrors the parts of CascadeLogic's <c>CascadeResult</c> wire shape (doc 03 §3) that
/// <see cref="GraphOrchestratorSteps"/> needs from the
/// <see cref="Abstractions.WorkflowActivityNames.EvaluateCascadeActivity"/> call. See
/// <see cref="CascadeEvalRequestPayload"/> for why this is duplicated rather than
/// referenced. <c>estimated_impact</c> is omitted — S2.2 re-walks the full regeneration
/// plan unconditionally; HITL gating on impact lands with S4.6. Unmapped JSON members
/// (including <c>estimated_impact</c>) are ignored on deserialization.
/// </summary>
public sealed record CascadeActivityResponse
{
    /// <summary>The section that was updated.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("changed_section")]
    public required string ChangedArtifact { get; init; }

    /// <summary>Artifacts to regenerate, in topological order — the regeneration plan.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("downstream_sections")]
    public required IReadOnlyList<string> DownstreamArtifacts { get; init; }

    /// <summary>Downstream sections with status <c>empty</c> — never generated, nothing to invalidate.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("skipped_sections")]
    public required IReadOnlyList<string> SkippedArtifacts { get; init; }
}
