using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The <c>model-role-config</c> container's append-only role-definition version document (ADR-PA40):
/// <c>{roleId}:definition:v{n}</c> in the role's <c>/role_id</c> partition, beside its mapping
/// versions. It never carries <c>mapping_version</c> and its id never starts with <c>{roleId}:v</c>,
/// so the mapping-version queries cannot pick it up. Never rewritten; a change is a new version.
/// </summary>
internal sealed record RoleDefinitionDocument
{
    /// <summary>The <c>doc_type</c> of a definition version document.</summary>
    internal const string DefinitionDocType = "role_definition";

    /// <summary>The deterministic version id: <c>{roleId}:definition:v{n}</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key (doc 08 §6: PK <c>/role_id</c>).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>Discriminates a definition version from the other documents in the role's partition.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("doc_type")]
    public string DocType { get; init; } = DefinitionDocType;

    /// <summary>This definition's monotonic version.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("definition_version")]
    public required int DefinitionVersion { get; init; }

    /// <summary>What serves the role; immutable for the role's life.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("backing")]
    public required RoleBacking Backing { get; init; }

    /// <summary>What the role is for.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>What is commercially at stake if the role's output is wrong.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("stakes")]
    public required StakesLevel Stakes { get; init; }

    /// <summary>The capability profile; omitted for an agent-backed role that states none.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("requirements")]
    public CapabilityRequirementsDocument? Requirements { get; init; }

    /// <summary>The input contract's ADR-E2 ref, if the role declares one.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("input_contract_ref")]
    public string? InputContractRef { get; init; }

    /// <summary>The input contract's hash, stamped by approval.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("input_contract_hash")]
    public string? InputContractHash { get; init; }

    /// <summary>The output contract's ADR-E2 ref, if the role declares one.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("output_contract_ref")]
    public string? OutputContractRef { get; init; }

    /// <summary>The output contract's hash, stamped by approval.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("output_contract_hash")]
    public string? OutputContractHash { get; init; }

    /// <summary>Who approved this version.</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("approved_by")]
    public required string ApprovedBy { get; init; }

    /// <summary>When this version took effect.</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("effective_from_utc")]
    public required DateTime EffectiveFromUtc { get; init; }

    /// <summary>Cosmos time-to-live in seconds; <c>-1</c> disables expiry.</summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("ttl")]
    public int Ttl { get; init; } = -1;

    /// <summary>Maps this wire document onto its domain <see cref="RoleDefinition"/>, refusing an ill-shaped definition on read.</summary>
    internal RoleDefinition ToDomain() => RoleDefinitionShape.EnsureValid(new RoleDefinition
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
    });

    /// <summary>
    /// Maps an approved <see cref="RoleDefinition"/> onto its version document. A definition without
    /// its approval stamps is refused: only an approved version is stored.
    /// </summary>
    internal static RoleDefinitionDocument FromDomain(RoleDefinition definition)
    {
        var version = definition.DefinitionVersion ?? throw Unstamped(definition, nameof(RoleDefinition.DefinitionVersion));
        return new RoleDefinitionDocument
        {
            Id = ModelRoleConfigDocumentId.ForDefinitionVersion(definition.RoleId, version),
            RoleId = definition.RoleId,
            DefinitionVersion = version,
            Backing = definition.Backing,
            Description = definition.Description,
            Stakes = definition.Stakes,
            Requirements = definition.Requirements is null ? null : CapabilityRequirementsDocument.FromDomain(definition.Requirements),
            InputContractRef = definition.InputContractRef,
            InputContractHash = definition.InputContractHash,
            OutputContractRef = definition.OutputContractRef,
            OutputContractHash = definition.OutputContractHash,
            ApprovedBy = definition.ApprovedBy ?? throw Unstamped(definition, nameof(RoleDefinition.ApprovedBy)),
            EffectiveFromUtc = definition.EffectiveFromUtc ?? throw Unstamped(definition, nameof(RoleDefinition.EffectiveFromUtc)),
        };
    }

    private static ArgumentException Unstamped(RoleDefinition definition, string stamp) =>
        new($"Role definition '{definition.RoleId}' has no {stamp}; only an approved definition is stored (ADR-PA40).", nameof(definition));
}
