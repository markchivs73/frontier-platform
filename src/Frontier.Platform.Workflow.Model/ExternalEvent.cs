using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// External event for engagement resolution (doc 16 §4, ADR-E6).
/// Input to EventResolutionService; typically sourced from webhook/event ingest endpoint.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Pure data contract; Validate() method not called at runtime.")]
public sealed record ExternalEvent
{
	/// <summary>Source system (e.g., "zendesk", "jira", "servicenow").</summary>
	public required string SourceSystem { get; init; }

	/// <summary>Event kind (e.g., "ticket-created", "issue-updated", "incident-opened").</summary>
	public required string EventKind { get; init; }

	/// <summary>External event ID for deduplication (idempotency key).</summary>
	public required string ExternalEventId { get; init; }

	/// <summary>Raw event payload as a flat/nested dictionary (for JSONPath extraction).</summary>
	public required Dictionary<string, object?> Payload { get; init; }

	/// <summary>When the event occurred on the source system (UTC).</summary>
	public System.DateTime? OccurredAtUtc { get; init; }

	/// <summary>Actor/system that generated the event (optional).</summary>
	public string? Actor { get; init; }

	public void Validate()
	{
		if (string.IsNullOrWhiteSpace(SourceSystem))
			throw new System.InvalidOperationException("ExternalEvent: SourceSystem is required");
		if (string.IsNullOrWhiteSpace(EventKind))
			throw new System.InvalidOperationException("ExternalEvent: EventKind is required");
		if (string.IsNullOrWhiteSpace(ExternalEventId))
			throw new System.InvalidOperationException("ExternalEvent: ExternalEventId is required");
		if (Payload == null)
			throw new System.InvalidOperationException("ExternalEvent: Payload is required");
	}
}
