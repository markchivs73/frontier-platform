using System.Text.Json.Serialization;

using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// A work item for dispatcher-mode workflows (doc 16 §4, ADR-E8). The item is received
/// as a <c>WorkItem</c> external event and spawned as a sub-orchestration with instanceId
/// <c>{engagementId}::{workflowId}::{workItemId}</c>. Phase 1: simple shape with ID + payload.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Pure data contract; Validate() method not called at runtime.")]
public sealed record WorkItem
{
    /// <summary>Unique identifier for this work item (forms part of child instanceId).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("work_item_id")]
    public required string WorkItemId { get; init; }

    /// <summary>Generic payload (the work-item input contract, type determined by engagement).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("payload")]
    public required object Payload { get; init; }

    /// <summary>
    /// The directing human behind this work item (ADR-E8, S13.19), threaded by the
    /// dispatcher into the child execution's <c>initiated_by</c> so per-item attribution
    /// survives the spawn. Null falls back to the dispatcher's own initiator.
    /// </summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("directed_by")]
    public string? DirectedBy { get; init; }
}
