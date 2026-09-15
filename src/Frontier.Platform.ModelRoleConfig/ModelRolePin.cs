using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// One role's mapping pinned at execution start (doc 08 §5, ADR-PA29): the version actually
/// <b>served</b> to this engagement — canary assignment and shadow/canary→fleet fallback already
/// applied — and that version's ring. Resolution under a pin reads exactly
/// <see cref="MappingVersion"/> and walks its fallback chain; it never re-evaluates rings.
/// <para>
/// Structured rather than a bare version so a shadow candidate can later ride alongside as
/// additive optional members (<c>candidate_version</c>/<c>candidate_ring</c>) without reshaping
/// anything recorded before them.
/// </para>
/// </summary>
public sealed record ModelRolePin
{
    /// <summary>The role this pin is for.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>The mapping version served to this execution for <see cref="RoleId"/>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("mapping_version")]
    public required int MappingVersion { get; init; }

    /// <summary>The ring of the served version at pin time.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("ring")]
    public required RolloutRing Ring { get; init; }
}
