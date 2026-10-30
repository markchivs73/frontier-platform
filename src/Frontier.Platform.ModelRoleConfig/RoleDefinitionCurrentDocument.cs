using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The <c>model-role-config</c> container's role-definition pointer (ADR-PA40):
/// <c>{roleId}:definition:current</c>, one per role, moved by each approved definition. Its
/// <c>doc_type</c> is what the catalogue query selects on, across role partitions. It carries
/// <c>definition_version</c>, never <c>mapping_version</c>, so it cannot be mistaken for the
/// mapping pointer or a mapping version.
/// </summary>
internal sealed record RoleDefinitionCurrentDocument
{
    /// <summary>The <c>doc_type</c> of a definition pointer document.</summary>
    internal const string CurrentDocType = "role_definition_current";

    /// <summary>The deterministic pointer id: <c>{roleId}:definition:current</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key (doc 08 §6: PK <c>/role_id</c>).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>Discriminates the definition pointer from the other documents in the role's partition.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("doc_type")]
    public string DocType { get; init; } = CurrentDocType;

    /// <summary>The <see cref="RoleDefinitionDocument.Id"/> this role's definition currently points to.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("current_ref")]
    public required string CurrentRef { get; init; }

    /// <summary>The definition version <see cref="CurrentRef"/> points to.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("definition_version")]
    public required int DefinitionVersion { get; init; }

    /// <summary>Cosmos time-to-live in seconds; <c>-1</c> disables expiry.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("ttl")]
    public int Ttl { get; init; } = -1;

    /// <summary>Builds the pointer for <paramref name="document"/>, pointing at its own version.</summary>
    internal static RoleDefinitionCurrentDocument For(RoleDefinitionDocument document) => new()
    {
        Id = ModelRoleConfigDocumentId.ForDefinitionCurrent(document.RoleId),
        RoleId = document.RoleId,
        CurrentRef = document.Id,
        DefinitionVersion = document.DefinitionVersion,
    };
}
