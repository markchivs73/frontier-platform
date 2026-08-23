using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Compiler.Schema;

/// <summary>
/// The workflow design-language schema served from <c>GET /api/definitions/schema</c>
/// (doc 14 §7, ADR-CD3). Describes the node types, edge shape, and enum value lists the
/// design agent reasons over so it proposes structurally valid definitions rather than
/// hallucinating node types or field names. Generated from
/// <c>Frontier.Reason.Workflow.Abstractions</c> at startup (S9.7, runtime generation —
/// the build-time embedded-resource pipeline remains the eventual ADR-CD3 target).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain projection DTO; field values are exercised by the generator tests.")]
public sealed record WorkflowSchema
{
    /// <summary>Format version of this schema document, distinct from any contract schema version.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public required string SchemaVersion { get; init; }

    /// <summary>The node types a <c>WorkflowDefinition</c> can contain, including deprecated ones.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("node_types")]
    public required IReadOnlyList<NodeTypeDescriptor> NodeTypes { get; init; }

    /// <summary>
    /// Field shapes for the complex contracts referenced by <c>object:&lt;Name&gt;</c> field type
    /// tokens (e.g. <c>ContextRequest</c>) — so the agent fills nested objects with real fields
    /// rather than guessing.
    /// </summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("objects")]
    public required IReadOnlyList<ObjectDescriptor> Objects { get; init; }

    /// <summary>The shape of a <c>WorkflowEdge</c> connecting two nodes.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("edge")]
    public required EdgeDescriptor Edge { get; init; }

    /// <summary>Enum value lists referenced by <c>enum:&lt;Name&gt;</c> field type tokens.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("enums")]
    public required IReadOnlyList<EnumDescriptor> Enums { get; init; }

    /// <summary>
    /// The valid data-contract type names (S9.72) — the only permitted values for an
    /// <c>agent_task</c> node's <c>input_contract_type</c>/<c>output_contract_type</c> and a
    /// <c>data</c> edge's <c>contract_type</c>. Sourced from the <c>IVersionedContract</c> types in
    /// Abstractions so the agent picks real contracts and can match edges to consumers.
    /// </summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("contracts")]
    public required IReadOnlyList<string> Contracts { get; init; }
}

/// <summary>Describes a complex contract referenced by an <c>object:&lt;Name&gt;</c> field token.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain projection DTO; field values are exercised by the generator tests.")]
public sealed record ObjectDescriptor
{
    /// <summary>The CLR type name, as referenced by <c>object:&lt;Name&gt;</c> field type tokens.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The object's fields, in canonical wire order.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("fields")]
    public required IReadOnlyList<FieldDescriptor> Fields { get; init; }
}

/// <summary>Describes one <c>WorkflowNode</c> subtype: its discriminator, intent, and fields.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain projection DTO; field values are exercised by the generator tests.")]
public sealed record NodeTypeDescriptor
{
    /// <summary>The wire discriminator value (e.g. <c>agent_task</c>), matching <c>node_type</c> on the JSON object.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("node_type")]
    public required string NodeType { get; init; }

    /// <summary>Human-readable intent, sourced from the type's XML-doc summary; <c>null</c> if undocumented.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Whether this node type is deprecated and should not be proposed for new workflows.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("deprecated")]
    public required bool Deprecated { get; init; }

    /// <summary>
    /// Whether the deployment's orchestrator can execute this node type (ADR-DC7, S13.7h).
    /// Additive-with-default <c>true</c>, so a schema consumer that predates the flag reads
    /// unchanged. A <c>false</c> here is the difference between a workflow that runs and one that
    /// validates, publishes, then fails permanently on its first execution.
    /// </summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("executable")]
    public bool Executable { get; init; } = true;

    /// <summary>The node's fields, in canonical wire order (inherited base fields first).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("fields")]
    public required IReadOnlyList<FieldDescriptor> Fields { get; init; }
}

/// <summary>Describes one field of a node or edge: its wire name, type token, optionality, and intent.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain projection DTO; field values are exercised by the generator tests.")]
public sealed record FieldDescriptor
{
    /// <summary>The snake_case wire name of the field.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The field's type token: <c>string</c>, <c>integer</c>, <c>boolean</c>,
    /// <c>array&lt;string&gt;</c>, <c>enum:&lt;Name&gt;</c> (resolves against <c>enums</c>),
    /// or <c>object:&lt;TypeName&gt;</c> (a complex contract referenced by name only).
    /// </summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Whether the field is required (non-nullable / <c>required</c> modifier).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("required")]
    public required bool Required { get; init; }

    /// <summary>Human-readable intent, sourced from the property's XML-doc summary; <c>null</c> if undocumented.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>A named enum and its canonical snake_case value list.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain projection DTO; field values are exercised by the generator tests.")]
public sealed record EnumDescriptor
{
    /// <summary>The enum's CLR type name, as referenced by <c>enum:&lt;Name&gt;</c> field type tokens.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The enum's allowed wire values, in declaration order.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("values")]
    public required IReadOnlyList<string> Values { get; init; }
}

/// <summary>Describes the <c>WorkflowEdge</c> shape (it has no subtypes, only fields).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain projection DTO; field values are exercised by the generator tests.")]
public sealed record EdgeDescriptor
{
    /// <summary>The edge's fields, in canonical wire order.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("fields")]
    public required IReadOnlyList<FieldDescriptor> Fields { get; init; }
}
