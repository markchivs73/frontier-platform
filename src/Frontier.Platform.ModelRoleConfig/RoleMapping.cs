namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// A versioned role→model mapping (doc 08 §4): a governed, auditable release, not a
/// config edit (doc 08 §2 principle 3). Stored as <c>{roleId}:v{mappingVersion}</c> in
/// the <c>model-role-config</c> container (doc 08 §6); an execution pins the active
/// <see cref="MappingVersion"/> for a role at start (doc 08 §5).
/// </summary>
public sealed record RoleMapping
{
    /// <summary>The role this mapping is for.</summary>
    public required string RoleId { get; init; }

    /// <summary>Monotonic version number for this role's mapping history.</summary>
    public required int MappingVersion { get; init; }

    /// <summary>The model chain: <c>[0]</c> is primary, the rest are ordered fallbacks (doc 08 §4 ADR-M2).</summary>
    public required IReadOnlyList<ModelEntry> Chain { get; init; }

    /// <summary>This mapping's rollout stage.</summary>
    public required RolloutRing Ring { get; init; }

    /// <summary>The percentage of new executions served this mapping when <see cref="Ring"/> is <see cref="RolloutRing.Canary"/>.</summary>
    public required int CanaryPercent { get; init; }

    /// <summary>Why this mapping was changed to (governance record, doc 08 §7).</summary>
    public required string ChangeReason { get; init; }

    /// <summary>Who approved this mapping (the <c>model-governance</c> role, doc 08 §8).</summary>
    public required string ApprovedBy { get; init; }

    /// <summary>When this mapping became active.</summary>
    public required DateTime EffectiveFromUtc { get; init; }

    /// <summary>Link to the empirical comparison that justified this mapping, if any (doc 08 §4).</summary>
    public string? EvaluationEvidenceRef { get; init; }

    /// <summary>
    /// The version of the most recent fleet-ring predecessor (doc 08 §5): non-null when
    /// <see cref="Ring"/> is <see cref="RolloutRing.Canary"/> or <see cref="RolloutRing.Shadow"/>,
    /// so <see cref="ModelResolver"/> can fall back to the fleet version for engagements not
    /// assigned to the canary ring. Null for fleet mappings.
    /// </summary>
    public int? PredecessorFleetVersion { get; init; }
}
