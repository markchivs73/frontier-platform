using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The <c>model-role-config</c> container's proposal document (ADR-PA32): one per proposal, in the
/// role's own <c>/role_id</c> partition beside the mapping versions it may become — which is what
/// lets an approval allocate a version and move the proposal in one transactional batch.
/// <para>
/// The document owns its own flat wire shape rather than nesting <see cref="MappingChangeProposal"/>,
/// exactly as <see cref="RoleMappingDocument"/> does for <see cref="RoleMapping"/>. That is not
/// ceremony: the domain records carry no <c>[JsonPropertyName]</c>, so nesting them would write
/// PascalCase keys into stored bytes and break the canonical profile's snake_case guarantee — and
/// stored bytes are evidential.
/// </para>
/// <para>
/// Unlike a <see cref="RoleMappingDocument"/> a proposal is <b>mutable</b>: it carries a state
/// machine, and each decision replaces it under an ETag guard. The immutable record of what went
/// live is the version document the approval creates, which is never rewritten.
/// </para>
/// </summary>
internal sealed record MappingProposalDocument
{
    /// <summary>The <c>doc_type</c> of a proposal document.</summary>
    internal const string ProposalDocType = "mapping_proposal";

    /// <summary>The deterministic proposal id: <c>{roleId}:proposal:{proposalId}</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key (doc 08 §6: PK <c>/role_id</c>).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>Discriminates a proposal from a version or pointer document in the same partition.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("doc_type")]
    public string DocType { get; init; } = ProposalDocType;

    /// <summary>The proposal's identifier.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("proposal_id")]
    public required string ProposalId { get; init; }

    /// <summary>Where the proposal sits in the governance loop.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("state")]
    public required MappingProposalState State { get; init; }

    /// <summary>Who proposed it (ADR-E8).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("proposed_by")]
    public required string ProposedBy { get; init; }

    /// <summary>When it was proposed.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("proposed_at_utc")]
    public required DateTime ProposedAtUtc { get; init; }

    /// <summary>Why the change is proposed (governance record, doc 08 §7).</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("change_reason")]
    public required string ChangeReason { get; init; }

    /// <summary>The ring the proposed mapping would enter.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("ring")]
    public required RolloutRing Ring { get; init; }

    /// <summary>The proposed canary percentage.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("canary_percent")]
    public required int CanaryPercent { get; init; }

    /// <summary>The proposed chain, in the container's own chain shape (ADR-PA27).</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("chain")]
    public required IReadOnlyList<ChainEntryDocument> Chain { get; init; }

    /// <summary>Link to the empirical comparison justifying the change, if any (doc 08 §4).</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("evaluation_evidence_ref")]
    public string? EvaluationEvidenceRef { get; init; }

    /// <summary>The role's fleet version when this was proposed (the S6.6 verdict).</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("predecessor_fleet_version")]
    public int? PredecessorFleetVersion { get; init; }

    /// <summary>The mapping version allocated on approval.</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("mapping_version")]
    public int? MappingVersion { get; init; }

    /// <summary>Who approved it, stamped by the service.</summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("approved_by")]
    public string? ApprovedBy { get; init; }

    /// <summary>When the approved mapping became effective.</summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName("effective_from_utc")]
    public DateTime? EffectiveFromUtc { get; init; }

    /// <summary>The fleet version allocated on promotion.</summary>
    [JsonPropertyOrder(16)]
    [JsonPropertyName("promoted_version")]
    public int? PromotedVersion { get; init; }

    /// <summary>Who last decided on the proposal.</summary>
    [JsonPropertyOrder(17)]
    [JsonPropertyName("decided_by")]
    public string? DecidedBy { get; init; }

    /// <summary>When that decision was taken.</summary>
    [JsonPropertyOrder(18)]
    [JsonPropertyName("decided_at_utc")]
    public DateTime? DecidedAtUtc { get; init; }

    /// <summary>Why that decision was taken.</summary>
    [JsonPropertyOrder(19)]
    [JsonPropertyName("decision_reason")]
    public string? DecisionReason { get; init; }

    /// <summary>The version a rollback returned the role to.</summary>
    [JsonPropertyOrder(20)]
    [JsonPropertyName("rolled_back_to_version")]
    public int? RolledBackToVersion { get; init; }

    /// <summary>Cosmos time-to-live in seconds; <c>-1</c> disables expiry (doc 08 §6 "TTL -1").</summary>
    [JsonPropertyOrder(21)]
    [JsonPropertyName("ttl")]
    public int Ttl { get; init; } = -1;

    /// <summary>Flattens <paramref name="proposal"/> onto its stored document.</summary>
    internal static MappingProposalDocument FromDomain(MappingChangeProposal proposal)
    {
        var mapping = proposal.Change.ProposedMapping;

        return new MappingProposalDocument
        {
            Id = ModelRoleConfigDocumentId.ForProposal(proposal.RoleId, proposal.ProposalId),
            RoleId = proposal.RoleId,
            ProposalId = proposal.ProposalId,
            State = proposal.State,
            ProposedBy = proposal.ProposedBy,
            ProposedAtUtc = proposal.ProposedAtUtc,
            ChangeReason = proposal.Change.Reason,
            Ring = mapping.Ring,
            CanaryPercent = mapping.CanaryPercent,
            Chain = [.. mapping.Chain.Select(ChainEntryDocument.FromDomain)],
            EvaluationEvidenceRef = mapping.EvaluationEvidenceRef,
            PredecessorFleetVersion = proposal.PredecessorFleetVersion,
            MappingVersion = proposal.MappingVersion,
            ApprovedBy = proposal.ApprovedBy,
            EffectiveFromUtc = proposal.EffectiveFromUtc,
            PromotedVersion = proposal.PromotedVersion,
            DecidedBy = proposal.DecidedBy,
            DecidedAtUtc = proposal.DecidedAtUtc,
            DecisionReason = proposal.DecisionReason,
            RolledBackToVersion = proposal.RolledBackToVersion,
        };
    }

    /// <summary>Rebuilds the proposal, refusing an ill-shaped stored chain on read (ADR-PA27).</summary>
    internal MappingChangeProposal ToDomain() => new()
    {
        ProposalId = ProposalId,
        RoleId = RoleId,
        State = State,
        ProposedBy = ProposedBy,
        ProposedAtUtc = ProposedAtUtc,
        PredecessorFleetVersion = PredecessorFleetVersion,
        MappingVersion = MappingVersion,
        ApprovedBy = ApprovedBy,
        EffectiveFromUtc = EffectiveFromUtc,
        PromotedVersion = PromotedVersion,
        DecidedBy = DecidedBy,
        DecidedAtUtc = DecidedAtUtc,
        DecisionReason = DecisionReason,
        RolledBackToVersion = RolledBackToVersion,
        Change = new MappingChange { RoleId = RoleId, Reason = ChangeReason, ProposedMapping = ToProposedMapping() },
    };

    /// <summary>The proposed mapping as stored: version 0 and no approver until an approval makes it real.</summary>
    internal RoleMapping ToProposedMapping() => ChainShape.EnsureValid(new RoleMapping
    {
        RoleId = RoleId,
        MappingVersion = 0,
        Chain = [.. Chain.Select(entry => entry.ToDomain())],
        Ring = Ring,
        CanaryPercent = CanaryPercent,
        ChangeReason = ChangeReason,
        ApprovedBy = string.Empty,
        EffectiveFromUtc = ProposedAtUtc,
        EvaluationEvidenceRef = EvaluationEvidenceRef,
        PredecessorFleetVersion = PredecessorFleetVersion,
    });
}
