using System.Text.Json.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Input to <see cref="GraphOrchestrator"/> (doc 00 §3.1, ADR-2) and <see cref="DispatcherOrchestrator"/> (S6.10, doc 00 §4.4).
/// The pinned definition rides inline from the Host factory; the orchestrator body never fetches it. For dispatcher-mode children,
/// <see cref="WorkItemId"/> is supplied by the parent dispatcher router and identifies the child as a field
/// (doc 16 §4, ADR-E8) — an instance id is an opaque run token and carries none of this (ADR-PA20).
/// </summary>
public sealed record GraphOrchestratorInput
{
    /// <summary>The pinned workflow definition for this execution.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("definition")]
    [JsonConverter(typeof(MigratingWorkflowDefinitionConverter))]
    public required WorkflowDefinition Definition { get; init; }

    /// <summary>The engagement this execution belongs to (forms the instance id, doc 12).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>
    /// For dispatcher-mode children: the work item ID that uniquely identifies this child execution within the dispatcher
    /// (the child's identity as a field, S6.10, ADR-E8 — not a component of its instance id, ADR-PA20).
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

    /// <summary>
    /// Discriminates this run from every other run of the same engagement-workflow (ADR-EX1).
    /// <b>Additive and optional</b> per the ADR-E15 compatibility floor: inputs and documents
    /// recorded before this field existed replay and read as <see langword="null"/>, which means
    /// "the single run of a pre-change execution" — never "unknown".
    /// </summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("run_id")]
    public string? RunId { get; init; }

    /// <summary>
    /// The dynamic-context epoch this run is pinned to (S13.60, doc 04 §4 step 3 / §8), resolved
    /// by the Host before scheduling — exactly as the definition is (ADR-2) — and carried inline
    /// so the orchestrator body reads no store. <b>Additive and optional</b> per the ADR-E15 floor:
    /// inputs recorded before this field replay as <see langword="null"/> ("read current"), which
    /// is what they did.
    /// </summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("dynamic_context_epoch")]
    public int? DynamicContextEpoch { get; init; }

    /// <summary>
    /// The store's content hash of that epoch, resolved alongside it, so the run's evidence is
    /// self-verifying without a store read (the definition's <c>definition_hash</c> shape).
    /// </summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("dynamic_context_hash")]
    public string? DynamicContextHash { get; init; }

    /// <summary>
    /// When <see langword="true"/>, <see cref="GraphOrchestrator"/> schedules
    /// <see cref="WorkflowActivityNames.PinMappingsActivity"/> as its first action and every agent
    /// invocation resolves under that pin (doc 08 §5, ADR-PA29). <b>Additive and optional</b> per the
    /// ADR-E15 floor, and a replay gate: the substrate matches activity names against history, so an
    /// input recorded without this flag (<see langword="null"/>) must replay without the pin and
    /// resolves the current mapping, exactly as it did.
    /// </summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("pin_model_roles")]
    public bool? PinModelRoles { get; init; }
}
