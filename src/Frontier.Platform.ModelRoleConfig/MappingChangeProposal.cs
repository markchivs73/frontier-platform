using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// A <see cref="MappingChange"/> recorded as a proposal and carried through the doc 08 §7
/// governance loop (ADR-PA32). Stored in <c>model-role-config</c> in the role's own
/// <c>/role_id</c> partition, beside the mapping versions it may become.
/// <para>
/// <see cref="ApprovedBy"/>, <see cref="EffectiveFromUtc"/> and <see cref="MappingVersion"/> are
/// stamped by <see cref="IMappingGovernanceService"/>, never taken from a caller. Until approval
/// there is no version to name, so <c>Change.ProposedMapping</c> carries
/// <see cref="RoleMapping.MappingVersion"/> 0 and an empty <see cref="RoleMapping.ApprovedBy"/>:
/// a proposed mapping is not a mapping until an approver makes it one.
/// </para>
/// </summary>
public sealed record MappingChangeProposal : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The proposal's identifier, used by <see cref="IMappingGovernanceService.ApproveAsync"/>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("proposal_id")]
    public required string ProposalId { get; init; }

    /// <summary>The role this proposal would remap.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>Where this proposal sits in the governance loop.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("state")]
    public required MappingProposalState State { get; init; }

    /// <summary>The proposed change.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("change")]
    public required MappingChange Change { get; init; }

    /// <summary>Who proposed it, from the authenticated principal (ADR-E8).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("proposed_by")]
    public required string ProposedBy { get; init; }

    /// <summary>When this proposal was recorded.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("proposed_at_utc")]
    public required DateTime ProposedAtUtc { get; init; }

    /// <summary>
    /// The role's fleet version when this was proposed (the S6.6 verdict): what an engagement not
    /// assigned to the canary ring keeps being served, and what a rollback would return to.
    /// Null only when the role has no fleet version yet.
    /// </summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("predecessor_fleet_version")]
    public int? PredecessorFleetVersion { get; init; }

    /// <summary>The mapping version allocated on approval; null while the proposal is undecided.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("mapping_version")]
    public int? MappingVersion { get; init; }

    /// <summary>Who approved it, stamped by the service. Never the proposer (distinct-approver rule).</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("approved_by")]
    public string? ApprovedBy { get; init; }

    /// <summary>When the approved mapping became effective, stamped by the service.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("effective_from_utc")]
    public DateTime? EffectiveFromUtc { get; init; }

    /// <summary>The fleet version allocated when this proposal was promoted; null until then.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("promoted_version")]
    public int? PromotedVersion { get; init; }

    /// <summary>Who last decided on this proposal — rejected, withdrew, promoted or rolled it back.</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("decided_by")]
    public string? DecidedBy { get; init; }

    /// <summary>When that decision was taken.</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("decided_at_utc")]
    public DateTime? DecidedAtUtc { get; init; }

    /// <summary>Why that decision was taken (governance record, doc 08 §7).</summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("decision_reason")]
    public string? DecisionReason { get; init; }

    /// <summary>The version a rollback returned the role to; null unless <see cref="State"/> is rolled back.</summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName("rolled_back_to_version")]
    public int? RolledBackToVersion { get; init; }

    /// <summary>
    /// The proposal's concurrency token as it was last read (ADR-PA34): the stored document's ETag,
    /// which a decision may pass back as its expected token so a decision taken on a stale view is
    /// refused rather than applied. Populated on every read — <c>GetProposalAsync</c>,
    /// <c>ListProposalsAsync</c>, and the proposal each decision returns.
    /// <para>
    /// <b>Optional, and never stored</b> (ADR-E15's additive floor): it is a property of the stored
    /// document rather than of the proposal, so it is not written into
    /// <c>MappingProposalDocument</c> — bytes recorded before this property read back with it null,
    /// and nothing downstream may require it.
    /// </para>
    /// </summary>
    [JsonPropertyOrder(16)]
    [JsonPropertyName("concurrency_token")]
    public string? ConcurrencyToken { get; init; }

    /// <inheritdoc />
    public void Validate() =>
        MappingProposalRules.ValidateProposal(this);
}
