using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// What a rollback did (doc 08 §8 ADR-M3, ADR-PA32). Returned by
/// <see cref="IMappingGovernanceService.RollbackToVersionAsync"/> so the caller can answer doc 08
/// §8's response without re-reading the store: which version was live, which is live now, and from
/// when. In-flight executions are unaffected — they resolve under the version they pinned at start
/// (ADR-PA29), which is the guarantee that makes an instant rollback safe.
/// </summary>
public sealed record MappingRollbackResult
{
    /// <summary>The role that was rolled back.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>The version <c>current</c> pointed at before the rollback.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("previous_version")]
    public required int PreviousVersion { get; init; }

    /// <summary>The version <c>current</c> points at now.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("current_version")]
    public required int CurrentVersion { get; init; }

    /// <summary>When the rollback took effect for new executions.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("effective_at_utc")]
    public required DateTime EffectiveAtUtc { get; init; }

    /// <summary>The ring of the version now current.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("ring")]
    public required RolloutRing Ring { get; init; }
}
