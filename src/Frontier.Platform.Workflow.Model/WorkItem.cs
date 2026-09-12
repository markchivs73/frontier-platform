using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// A work item for dispatcher-mode workflows (doc 16 §4, ADR-E8). The item arrives as a
/// <c>WorkItem</c> external event and is spawned as a sub-orchestration identified by its
/// <see cref="WorkItemId"/> field, never by a composite instance id (ADR-PA15/PA20).
/// <para>
/// <b>The payload is an ADR-E2 envelope, not <c>object</c>.</b> This is the one contract that
/// carries <em>external</em> input across the DTF history boundary, where the bytes are
/// evidential and replayed for the life of an eternal instance. <c>object</c> deserializes as a
/// <see cref="System.Text.Json.JsonElement"/>, which leaves every consumer re-inspecting an
/// untyped blob and gives ADR-E1 tonnage (a large payload staged by reference) nowhere to live.
/// <see cref="TypedPayload"/> is the engine's one generic carriage for exactly that, inline or by
/// reference, naming the schema its content conforms to.
/// </para>
/// </summary>
public sealed record WorkItem : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Uniquely identifies this work item, and the child execution spawned for it.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("work_item_id")]
    public required string WorkItemId { get; init; }

    /// <summary>The work item's content as an ADR-E2 typed envelope — inline, or staged by reference.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("payload")]
    public required TypedPayload Payload { get; init; }

    /// <summary>
    /// The directing human behind this work item (ADR-E8, S13.19), threaded by the
    /// dispatcher into the child execution's <c>initiated_by</c> so per-item attribution
    /// survives the spawn. Null falls back to the dispatcher's own initiator — a
    /// machine-originated item has no directing human of its own.
    /// </summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("directed_by")]
    public string? DirectedBy { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(WorkItemId))
        {
            violations.Add("work_item_id must not be blank.");
        }

        CollectPayloadViolations(violations);

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(WorkItem), violations);
        }
    }

    /// <summary>
    /// Cascades <see cref="Payload"/>'s own validation, prefixing its violations. The envelope's
    /// rules are the work item's rules: a payload claiming both inline content and a staged
    /// reference is malformed wherever it appears, and accepting it here would hand a child
    /// execution a contract the child must then reject — a permanent failure (invariant 7)
    /// discovered one instance too late.
    /// </summary>
    internal void CollectPayloadViolations(List<string> violations)
    {
        try
        {
            Payload.Validate();
        }
        catch (ContractViolationException ex)
        {
            violations.AddRange(ex.Violations.Select(violation => $"payload: {violation}"));
        }
    }
}
