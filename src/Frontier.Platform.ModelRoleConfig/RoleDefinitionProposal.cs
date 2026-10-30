using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// A proposed new role, or a change to one, carried through the definition governance loop
/// (ADR-PA40): propose, then approve, reject or withdraw. There is no promote and no rollback; to
/// revert, propose the old definition again. Stored in <c>model-role-config</c> in the role's own
/// <c>/role_id</c> partition as <c>{roleId}:definition-proposal:{id}</c>.
/// <para>
/// It reuses <see cref="MappingProposalState"/> but only ever enters
/// <see cref="MappingProposalState.PendingApproval"/>, <see cref="MappingProposalState.Approved"/>,
/// <see cref="MappingProposalState.Rejected"/> and <see cref="MappingProposalState.Withdrawn"/>.
/// <see cref="DefinitionVersion"/>, <see cref="ApprovedBy"/>, <see cref="EffectiveFromUtc"/> and the
/// contract hashes on <see cref="ProposedDefinition"/> are stamped by
/// <see cref="IRoleDefinitionGovernanceService"/>, never taken from a caller (ADR-PA32's rule).
/// </para>
/// </summary>
public sealed record RoleDefinitionProposal : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The proposal's identifier, which every decision names.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("proposal_id")]
    public required string ProposalId { get; init; }

    /// <summary>The role this proposal would define or change.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>Where this proposal sits in the governance loop.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("state")]
    public required MappingProposalState State { get; init; }

    /// <summary>
    /// The definition that becomes the role's current one if this is approved. Unstamped while the
    /// proposal is pending; approval stamps its version, approver, effective-at and contract hashes.
    /// </summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("proposed_definition")]
    public required RoleDefinition ProposedDefinition { get; init; }

    /// <summary>Why the definition is proposed (governance record).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>Who proposed it, from the authenticated principal (ADR-E8).</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("proposed_by")]
    public required string ProposedBy { get; init; }

    /// <summary>When this proposal was recorded.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("proposed_at_utc")]
    public required DateTime ProposedAtUtc { get; init; }

    /// <summary>The role's current definition version when this was proposed; null for a new role.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("predecessor_definition_version")]
    public int? PredecessorDefinitionVersion { get; init; }

    /// <summary>The definition version allocated on approval; null until then.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("definition_version")]
    public int? DefinitionVersion { get; init; }

    /// <summary>Who approved it, stamped by the service. Never the proposer (distinct-approver rule).</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("approved_by")]
    public string? ApprovedBy { get; init; }

    /// <summary>When the approved definition took effect, stamped by the service.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("effective_from_utc")]
    public DateTime? EffectiveFromUtc { get; init; }

    /// <summary>Who decided on this proposal: approved, rejected or withdrew it.</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("decided_by")]
    public string? DecidedBy { get; init; }

    /// <summary>When that decision was taken.</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("decided_at_utc")]
    public DateTime? DecidedAtUtc { get; init; }

    /// <summary>Why that decision was taken.</summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("decision_reason")]
    public string? DecisionReason { get; init; }

    /// <summary>
    /// The stored document's ETag as last read (ADR-PA34), which a decision may pass back so a
    /// decision on a stale view is refused. Populated on every read and never stored.
    /// </summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName("concurrency_token")]
    public string? ConcurrencyToken { get; init; }

    /// <inheritdoc />
    public void Validate() =>
        RoleDefinitionProposalRules.ValidateProposal(this);
}

/// <summary>A page of role-definition proposals, newest first.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract.")]
public sealed record RoleDefinitionProposalPage
{
    /// <summary>The proposals, most recently proposed first.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("proposals")]
    public required IReadOnlyList<RoleDefinitionProposal> Proposals { get; init; }

    /// <summary>Pass back to read the next page; <see langword="null"/> on the last page.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("continuation_token")]
    public string? ContinuationToken { get; init; }
}
