using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// A role's append-only mapping history (doc 08 §6, ADR-PA34): every version ever written, with the
/// one the <c>current</c> pointer names marked.
/// <para>
/// <b>A new interface rather than a member on a shipped one.</b> Version listing already existed on
/// the internal <c>IMappingProposalStore</c>, but D3's version-history rows need it publicly.
/// Adding it to <see cref="IRoleRegistry"/> — which consumers' test doubles implement — would have
/// broken every implementor, which is the same reasoning that produced
/// <see cref="IMappingPinner"/> (ADR-PA29). A new interface costs one registration and breaks
/// nobody.
/// </para>
/// </summary>
public interface IMappingVersionHistory
{
    /// <summary>
    /// Every stored mapping version for <paramref name="roleId"/>, ascending by version. Empty when
    /// the role has none. The history is read-only and append-only: a version rolled away from stays
    /// in the list, because it is evidence of what served.
    /// </summary>
    Task<IReadOnlyList<MappingVersionSummary>> GetVersionHistoryAsync(string roleId, CancellationToken cancellationToken);
}

/// <summary>
/// One row of a role's mapping history (ADR-PA34) — exactly doc 20's <c>version_history</c> entry:
/// the version, the ring it was released into, its canary exposure, its chain, why it was changed,
/// who approved it, when it took effect, and whether it is the version serving now.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; its projection from RoleMapping is covered by MappingVersionHistory's tests.")]
public sealed record MappingVersionSummary
{
    /// <summary>The mapping version this row describes.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("mapping_version")]
    public required int MappingVersion { get; init; }

    /// <summary>The ring this version was released into. A property of the version, not of the role (doc 08 §4).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("ring")]
    public required RolloutRing Ring { get; init; }

    /// <summary>The canary exposure this version carried; 0 for a fleet version.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("canary_percent")]
    public required int CanaryPercent { get; init; }

    /// <summary>The chain this version served: <c>[0]</c> primary, the rest ordered fallbacks.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("chain")]
    public required IReadOnlyList<ChainEntry> Chain { get; init; }

    /// <summary>Why this version was released — the deciding actor's reason, recorded at approval.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("change_reason")]
    public required string ChangeReason { get; init; }

    /// <summary>Who approved this version.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("approved_by")]
    public required string ApprovedBy { get; init; }

    /// <summary>When this version became effective.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("effective_from_utc")]
    public required DateTime EffectiveFromUtc { get; init; }

    /// <summary>Whether the role's <c>current</c> pointer names this version.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("is_current")]
    public required bool IsCurrent { get; init; }
}
