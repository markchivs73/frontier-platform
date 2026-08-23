using System.Text.Json.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Input to <see cref="GraphOrchestrator"/> (doc 00 §3.1, ADR-2) and <see cref="DispatcherOrchestrator"/> (S6.10, doc 00 §4.4).
/// The pinned definition rides inline from the Host factory; the orchestrator body never fetches it. For dispatcher-mode children,
/// <see cref="WorkItemId"/> is supplied by the parent dispatcher router and forms part of the child's instance id
/// (<c>{engagementId}::{workflowId}::{workItemId}</c>, doc 16 §4, ADR-E8).
/// </summary>
public sealed record GraphOrchestratorInput
{
    /// <summary>The pinned workflow definition for this execution.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("definition")]
    public required WorkflowDefinition Definition { get; init; }

    /// <summary>The engagement this execution belongs to (forms the instance id, doc 12).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>
    /// For dispatcher-mode children: the work item ID that uniquely identifies this child execution within the dispatcher
    /// (forms the last component of child instanceId: <c>{engagementId}::{workflowId}::{workItemId}</c>, S6.10, ADR-E8).
    /// Null for OneShot mode (top-level executions started by the factory).
    /// </summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("work_item_id")]
    public string? WorkItemId { get; init; }

    /// <summary>
    /// The directing human this execution runs for (ADR-E8, S13.19) — threaded from the
    /// API caller's claims (or a work item's <c>directed_by</c> for dispatcher children)
    /// into every snapshot, so agent/tool actions attribute derivatively through an
    /// unbroken chain. Nullable and additive: inputs recorded before this field replay
    /// as null.
    /// </summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("initiated_by")]
    public string? InitiatedBy { get; init; }
}
