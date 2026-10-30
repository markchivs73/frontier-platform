using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The <c>model-role-config</c> container's role-definition proposal document (ADR-PA40):
/// <c>{roleId}:definition-proposal:{id}</c> in the role's <c>/role_id</c> partition, beside the
/// definition versions it may become, so an approval can allocate a version, move the proposal and
/// repoint the definition pointer in one transactional batch.
/// <para>
/// Flat, as <see cref="MappingProposalDocument"/> is: the domain <see cref="RoleDefinition"/> carries
/// no <c>[JsonPropertyName]</c>, so nesting it would write PascalCase keys into stored bytes. It never
/// carries <c>mapping_version</c>, so the mapping-version queries cannot pick it up. The one mutable
/// definition document: each decision replaces it under an ETag guard.
/// </para>
/// </summary>
internal sealed record RoleDefinitionProposalDocument
{
    /// <summary>The <c>doc_type</c> of a definition proposal document.</summary>
    internal const string ProposalDocType = "role_definition_proposal";

    /// <summary>The deterministic proposal id: <c>{roleId}:definition-proposal:{proposalId}</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key (doc 08 §6: PK <c>/role_id</c>).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>Discriminates a definition proposal from the other documents in the role's partition.</summary>
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

    /// <summary>Why the definition is proposed.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("change_reason")]
    public required string ChangeReason { get; init; }

    /// <summary>What would serve the role.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("backing")]
    public required RoleBacking Backing { get; init; }

    /// <summary>What the role is for.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>What is commercially at stake if the role's output is wrong.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("stakes")]
    public required StakesLevel Stakes { get; init; }

    /// <summary>The capability profile; omitted for an agent-backed role that states none.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("requirements")]
    public CapabilityRequirementsDocument? Requirements { get; init; }

    /// <summary>The input contract's ADR-E2 ref, if declared.</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("input_contract_ref")]
    public string? InputContractRef { get; init; }

    /// <summary>The output contract's ADR-E2 ref, if declared.</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("output_contract_ref")]
    public string? OutputContractRef { get; init; }

    /// <summary>The role's current definition version when this was proposed.</summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("predecessor_definition_version")]
    public int? PredecessorDefinitionVersion { get; init; }

    /// <summary>The definition version allocated on approval.</summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName("definition_version")]
    public int? DefinitionVersion { get; init; }

    /// <summary>The input contract's hash, stamped by approval.</summary>
    [JsonPropertyOrder(16)]
    [JsonPropertyName("input_contract_hash")]
    public string? InputContractHash { get; init; }

    /// <summary>The output contract's hash, stamped by approval.</summary>
    [JsonPropertyOrder(17)]
    [JsonPropertyName("output_contract_hash")]
    public string? OutputContractHash { get; init; }

    /// <summary>Who approved it, stamped by the service.</summary>
    [JsonPropertyOrder(18)]
    [JsonPropertyName("approved_by")]
    public string? ApprovedBy { get; init; }

    /// <summary>When the approved definition took effect.</summary>
    [JsonPropertyOrder(19)]
    [JsonPropertyName("effective_from_utc")]
    public DateTime? EffectiveFromUtc { get; init; }

    /// <summary>Who decided on the proposal.</summary>
    [JsonPropertyOrder(20)]
    [JsonPropertyName("decided_by")]
    public string? DecidedBy { get; init; }

    /// <summary>When that decision was taken.</summary>
    [JsonPropertyOrder(21)]
    [JsonPropertyName("decided_at_utc")]
    public DateTime? DecidedAtUtc { get; init; }

    /// <summary>Why that decision was taken.</summary>
    [JsonPropertyOrder(22)]
    [JsonPropertyName("decision_reason")]
    public string? DecisionReason { get; init; }

    /// <summary>Cosmos time-to-live in seconds; <c>-1</c> disables expiry.</summary>
    [JsonPropertyOrder(23)]
    [JsonPropertyName("ttl")]
    public int Ttl { get; init; } = -1;

    /// <summary>
    /// The Cosmos-assigned concurrency token (ADR-PA34), read back on every read and never set by
    /// <see cref="FromDomain"/>, so it is absent from written bytes.
    /// </summary>
    [JsonPropertyOrder(24)]
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }

    /// <summary>Flattens <paramref name="proposal"/> onto its stored document.</summary>
    internal static RoleDefinitionProposalDocument FromDomain(RoleDefinitionProposal proposal)
    {
        var definition = proposal.ProposedDefinition;
        return new RoleDefinitionProposalDocument
        {
            Id = ModelRoleConfigDocumentId.ForDefinitionProposal(proposal.RoleId, proposal.ProposalId),
            RoleId = proposal.RoleId,
            ProposalId = proposal.ProposalId,
            State = proposal.State,
            ProposedBy = proposal.ProposedBy,
            ProposedAtUtc = proposal.ProposedAtUtc,
            ChangeReason = proposal.Reason,
            Backing = definition.Backing,
            Description = definition.Description,
            Stakes = definition.Stakes,
            Requirements = definition.Requirements is null ? null : CapabilityRequirementsDocument.FromDomain(definition.Requirements),
            InputContractRef = definition.InputContractRef,
            OutputContractRef = definition.OutputContractRef,
            PredecessorDefinitionVersion = proposal.PredecessorDefinitionVersion,
            DefinitionVersion = proposal.DefinitionVersion,
            InputContractHash = definition.InputContractHash,
            OutputContractHash = definition.OutputContractHash,
            ApprovedBy = proposal.ApprovedBy,
            EffectiveFromUtc = proposal.EffectiveFromUtc,
            DecidedBy = proposal.DecidedBy,
            DecidedAtUtc = proposal.DecidedAtUtc,
            DecisionReason = proposal.DecisionReason,
        };
    }

    /// <summary>Rebuilds the proposal, refusing an ill-shaped stored definition on read (ADR-PA40).</summary>
    internal RoleDefinitionProposal ToDomain() => new()
    {
        ProposalId = ProposalId,
        RoleId = RoleId,
        State = State,
        ProposedDefinition = RoleDefinitionShape.EnsureValid(ToProposedDefinition()),
        Reason = ChangeReason,
        ProposedBy = ProposedBy,
        ProposedAtUtc = ProposedAtUtc,
        PredecessorDefinitionVersion = PredecessorDefinitionVersion,
        DefinitionVersion = DefinitionVersion,
        ApprovedBy = ApprovedBy,
        EffectiveFromUtc = EffectiveFromUtc,
        DecidedBy = DecidedBy,
        DecidedAtUtc = DecidedAtUtc,
        DecisionReason = DecisionReason,
    };

    /// <summary>The proposed definition as stored, carrying the approval stamps once it has them.</summary>
    internal RoleDefinition ToProposedDefinition() => new()
    {
        RoleId = RoleId,
        Description = Description,
        Stakes = Stakes,
        Requirements = Requirements?.ToDomain(),
        Backing = Backing,
        InputContractRef = InputContractRef,
        InputContractHash = InputContractHash,
        OutputContractRef = OutputContractRef,
        OutputContractHash = OutputContractHash,
        DefinitionVersion = DefinitionVersion,
        ApprovedBy = ApprovedBy,
        EffectiveFromUtc = EffectiveFromUtc,
    };
}
