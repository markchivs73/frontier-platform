using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// One DTF history event, mapped from the instance's orchestration history (doc 05 §4
/// step 1). <see cref="AuditRecord.OrchestrationEvents"/> orders these by DTF sequence —
/// the spine of the consolidated timeline.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record WorkflowEvent
{
    /// <summary>The DTF history event kind.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("event_type")]
    public required WorkflowEventType EventType { get; init; }

    /// <summary>The node this event relates to, recovered from the activity's input envelope, if any.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    /// <summary>The correlation id stamped on the activity's input envelope, if any (doc 05 §4 step 3 join key).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; init; }

    /// <summary>UTC timestamp at which the event occurred.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("occurred_at_utc")]
    public required DateTime OccurredAtUtc { get; init; }

    /// <summary>Free-text detail, e.g. a failure message for <see cref="WorkflowEventType.TaskFailed"/>.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("details")]
    public string? Details { get; init; }
}
