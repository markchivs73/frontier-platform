using System.Text.Json;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// The engine's one generic carriage for capability data (ADR-E2, DESIGN-DECISIONS.md S13.2):
/// content conforming to a capability-declared, discovery-surfaced schema rides either inline
/// (<see cref="Payload"/>, small content) or by reference (<see cref="PayloadRef"/>, staged
/// tonnage per ADR-E1) — exactly one of the two. <see cref="Facts"/> carries small inline
/// values (counts, flags) beside a ref only; an inline payload carries its own facts. Domain
/// shapes are never CLR contracts — <see cref="SchemaRef"/> names the JSON Schema
/// (2020-12, convention <c>{namespace}/{name}/{major}.{minor}</c>) that the content conforms
/// to, matched by the compiler on exact id + major version.
/// </summary>
public sealed record TypedPayload : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The capability-declared schema this content conforms to, e.g. <c>"schemas/document-structure/1.0"</c>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("schema_ref")]
    public required string SchemaRef { get; init; }

    /// <summary>Inline content (small payloads). Mutually exclusive with <see cref="PayloadRef"/>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }

    /// <summary>Staged content by reference (tonnage, ADR-E1). Mutually exclusive with <see cref="Payload"/>.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("payload_ref")]
    public PayloadRef? PayloadRef { get; init; }

    /// <summary>Small inline facts (counts, flags) accompanying a ref; only valid alongside <see cref="PayloadRef"/>.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("facts")]
    public JsonElement? Facts { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(SchemaRef))
        {
            violations.Add("schema_ref must not be empty.");
        }

        if (Payload is null == PayloadRef is null)
        {
            violations.Add("typed_payload must carry exactly one of payload or payload_ref.");
        }

        if (Facts is not null && PayloadRef is null)
        {
            violations.Add("facts is only valid alongside payload_ref — an inline payload carries its own facts.");
        }

        CollectPayloadRefViolations(violations);

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(TypedPayload), violations);
        }
    }

    /// <summary>Cascades nested <see cref="PayloadRef"/> validation, prefixing its violations.</summary>
    internal void CollectPayloadRefViolations(List<string> violations)
    {
        if (PayloadRef is null)
        {
            return;
        }

        try
        {
            PayloadRef.Validate();
        }
        catch (ContractViolationException ex)
        {
            violations.AddRange(ex.Violations.Select(v => $"payload_ref: {v}"));
        }
    }
}
