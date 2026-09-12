using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Input to <see cref="ResolveDispatcherVersionActivity"/> (S13.18, ADR-E15 D2): the pin the next
/// generation's definition is resolved against, projected from the dispatcher's own pinned input
/// at the <c>ContinueAsNew</c> boundary.
/// <para>
/// <see cref="CurrentDefinitionVersion"/> travels so the resolver can answer "nothing newer"
/// without the engine having to compare definitions itself — what counts as a successor is doc 16
/// §8's knowledge, not the interpreter's.
/// </para>
/// </summary>
public sealed record ResolveDispatcherVersionRequest
{
    /// <summary>The engagement whose per-engagement pin (doc 16 §8) governs the resolution.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The workflow whose published versions are being resolved.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("workflow_id")]
    public required string WorkflowId { get; init; }

    /// <summary>The definition version the generation now ending was pinned to.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("current_definition_version")]
    public required int CurrentDefinitionVersion { get; init; }
}
