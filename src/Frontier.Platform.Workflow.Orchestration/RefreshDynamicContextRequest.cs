using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Input to <see cref="RefreshDynamicContextActivity"/> (S13.62, doc 04 §8): what the orchestrator
/// decided to refresh, projected from the <see cref="Abstractions.DynamicContextRefreshRequired"/>
/// signal it consumed.
/// <para>
/// The engagement comes from <see cref="GraphOrchestratorInput.EngagementId"/> — the engagement the
/// run actually belongs to — not from the signal's own field, which is the ingest surface's routing
/// key. The two agree in practice; the run's own identity is the one that may not be taken on
/// trust from an inbound event.
/// </para>
/// </summary>
public sealed record RefreshDynamicContextRequest
{
    /// <summary>The engagement whose dynamic context is being refreshed.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>ADR-CR1's explicit reason, carried from the signal onto the refresh's metrics and logs.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>The engagement fields the Sense layer saw change, for audit and observability.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("changed_fields")]
    public IReadOnlyList<string> ChangedFields { get; init; } = [];

    /// <summary>
    /// The snake_case component keys to re-render, or <see langword="null"/> when the signal named
    /// none — in which case the producer decides what a refresh for this reason covers.
    /// </summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("components")]
    public IReadOnlyList<string>? Components { get; init; }
}
