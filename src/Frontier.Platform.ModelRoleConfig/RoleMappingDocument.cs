using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The <c>model-role-config</c> container's append-only mapping-version document shape
/// (doc 08 §6): one immutable record per <see cref="RoleMapping.MappingVersion"/>, never
/// rewritten — rollback (doc 08 §8 ADR-M3) repoints <see cref="RoleMappingCurrentDocument"/>
/// at an earlier version, not by editing this document.
/// </summary>
internal sealed record RoleMappingDocument
{
    /// <summary>The deterministic version id: <c>{roleId}:v{mappingVersion}</c> (doc 08 §6).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key (doc 08 §6: PK <c>/roleId</c>, the config-store exception to ADR-S1).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>This mapping's monotonic version number.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("mapping_version")]
    public required int MappingVersion { get; init; }

    /// <summary>This mapping's rollout stage.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("ring")]
    public required RolloutRing Ring { get; init; }

    /// <summary>The percentage of new executions served this mapping when <see cref="Ring"/> is <see cref="RolloutRing.Canary"/>.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("canary_percent")]
    public required int CanaryPercent { get; init; }

    /// <summary>The model chain: <c>[0]</c> is primary, the rest are ordered fallbacks (doc 08 §4 ADR-M2).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("chain")]
    public required IReadOnlyList<ModelEntryDocument> Chain { get; init; }

    /// <summary>Why this mapping was changed to (governance record, doc 08 §7).</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("change_reason")]
    public required string ChangeReason { get; init; }

    /// <summary>Who approved this mapping (the <c>model-governance</c> role, doc 08 §8).</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("approved_by")]
    public required string ApprovedBy { get; init; }

    /// <summary>When this mapping became active.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("effective_from_utc")]
    public required DateTime EffectiveFromUtc { get; init; }

    /// <summary>Link to the empirical comparison that justified this mapping, if any (doc 08 §4).</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("evaluation_evidence_ref")]
    public string? EvaluationEvidenceRef { get; init; }

    /// <summary>
    /// The version of the most recent fleet-ring predecessor (doc 08 §5): non-null for
    /// canary/shadow rings, null for fleet (omitted from the wire document when null per
    /// the canonical profile's omit-null rule).
    /// </summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("predecessor_fleet_version")]
    public int? PredecessorFleetVersion { get; init; }

    /// <summary>Cosmos time-to-live in seconds; <c>-1</c> disables expiry (doc 08 §6 "TTL -1").</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("ttl")]
    public int Ttl { get; init; } = -1;

    /// <summary>Maps this wire document onto its domain <see cref="RoleMapping"/>.</summary>
    internal RoleMapping ToDomain() => new()
    {
        RoleId = RoleId,
        MappingVersion = MappingVersion,
        Chain = Chain.Select(entry => entry.ToDomain()).ToArray(),
        Ring = Ring,
        CanaryPercent = CanaryPercent,
        ChangeReason = ChangeReason,
        ApprovedBy = ApprovedBy,
        EffectiveFromUtc = EffectiveFromUtc,
        EvaluationEvidenceRef = EvaluationEvidenceRef,
        PredecessorFleetVersion = PredecessorFleetVersion,
    };

    /// <summary>Maps a domain <see cref="RoleMapping"/> onto its append-only version document.</summary>
    internal static RoleMappingDocument FromDomain(RoleMapping mapping) => new()
    {
        Id = ModelRoleConfigDocumentId.ForVersion(mapping.RoleId, mapping.MappingVersion),
        RoleId = mapping.RoleId,
        MappingVersion = mapping.MappingVersion,
        Ring = mapping.Ring,
        CanaryPercent = mapping.CanaryPercent,
        Chain = mapping.Chain.Select(ModelEntryDocument.FromDomain).ToArray(),
        ChangeReason = mapping.ChangeReason,
        ApprovedBy = mapping.ApprovedBy,
        EffectiveFromUtc = mapping.EffectiveFromUtc,
        EvaluationEvidenceRef = mapping.EvaluationEvidenceRef,
        PredecessorFleetVersion = mapping.PredecessorFleetVersion,
    };
}
