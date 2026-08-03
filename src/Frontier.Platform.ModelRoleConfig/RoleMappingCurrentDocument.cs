using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The <c>model-role-config</c> container's mutable <c>current</c> pointer document
/// shape (doc 08 §6): one per role, repointed at a <see cref="RoleMappingDocument"/> on
/// every approved change (doc 08 §7) and on rollback (doc 08 §8 ADR-M3) — without
/// rewriting that version document.
/// </summary>
internal sealed record RoleMappingCurrentDocument
{
    /// <summary>The deterministic pointer id: <c>{roleId}:current</c> (doc 08 §6).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key (doc 08 §6: PK <c>/roleId</c>).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>The <see cref="RoleMappingDocument.Id"/> this role currently points to.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("current_ref")]
    public required string CurrentRef { get; init; }

    /// <summary>The mapping version <see cref="CurrentRef"/> points to.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("mapping_version")]
    public required int MappingVersion { get; init; }

    /// <summary>Cosmos time-to-live in seconds; <c>-1</c> disables expiry (doc 08 §6 "TTL -1").</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("ttl")]
    public int Ttl { get; init; } = -1;

    /// <summary>Builds the <c>current</c> pointer document for <paramref name="mapping"/>, pointing at its own version.</summary>
    internal static RoleMappingCurrentDocument FromDomain(RoleMapping mapping) => new()
    {
        Id = ModelRoleConfigDocumentId.ForCurrent(mapping.RoleId),
        RoleId = mapping.RoleId,
        CurrentRef = ModelRoleConfigDocumentId.ForVersion(mapping.RoleId, mapping.MappingVersion),
        MappingVersion = mapping.MappingVersion,
    };
}
