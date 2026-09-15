using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Input to <see cref="PinMappingsActivity"/> (doc 08 §5, ADR-PA29): the engagement (for
/// engagement-stable canary assignment) and every role the pinned definition's agent nodes use,
/// deduplicated and ordinally sorted by the orchestrator.
/// </summary>
public sealed record PinMappingsRequest
{
    /// <summary>The engagement the execution runs for.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The role ids to pin.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("role_ids")]
    public required IReadOnlyList<string> RoleIds { get; init; }
}
