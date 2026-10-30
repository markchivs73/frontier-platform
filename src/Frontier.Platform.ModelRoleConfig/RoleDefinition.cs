using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// A catalogue entry describing what a role is for and what it needs (doc 08 §4):
/// agents and workflow nodes declare a <see cref="RoleId"/>, never a model ID (doc 08 §2
/// principle 1 — total indirection). From v0.45.0 a role is a governed, versioned definition
/// stored in <c>model-role-config</c> (ADR-PA40), not a code constant.
/// </summary>
public sealed record RoleDefinition
{
    /// <summary>
    /// The role identifier (e.g. <c>"deep-reasoning"</c>) — the only thing a <c>AgentTaskNode.Role</c>
    /// may reference. Lowercase kebab-case, at most 64 characters, so never containing <c>:</c> (ADR-PA40).
    /// </summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>Human-readable description of what this role is for and which agents use it.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>What's commercially at stake if this role's output is wrong — drives mapping governance posture.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("stakes")]
    public required StakesLevel Stakes { get; init; }

    /// <summary>
    /// The capability profile any model in this role's mapping chain must satisfy. Required for a
    /// <see cref="RoleBacking.Model"/> role; optional for an <see cref="RoleBacking.Agent"/> one (ADR-PA40).
    /// </summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("requirements")]
    public CapabilityRequirements? Requirements { get; init; }

    /// <summary>What serves this role; declared once and immutable (ADR-PA40). Defaults to <see cref="RoleBacking.Model"/>.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("backing")]
    public RoleBacking Backing { get; init; } = RoleBacking.Model;

    /// <summary>The ADR-E2 ref (<c>{namespace}/{name}/{major}.{minor}</c>) of the contract this role takes; required for an agent-backed role.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("input_contract_ref")]
    public string? InputContractRef { get; init; }

    /// <summary>The hash of <see cref="InputContractRef"/>'s registered contract, stamped by approval and never taken from a caller.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("input_contract_hash")]
    public string? InputContractHash { get; init; }

    /// <summary>The ADR-E2 ref of the contract this role returns; required for an agent-backed role.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("output_contract_ref")]
    public string? OutputContractRef { get; init; }

    /// <summary>The hash of <see cref="OutputContractRef"/>'s registered contract, stamped by approval and never taken from a caller.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("output_contract_hash")]
    public string? OutputContractHash { get; init; }

    /// <summary>This definition's monotonic version, stamped by approval; null on a definition not yet approved.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("definition_version")]
    public int? DefinitionVersion { get; init; }

    /// <summary>Who approved this definition version, stamped by approval; null on a definition not yet approved.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("approved_by")]
    public string? ApprovedBy { get; init; }

    /// <summary>When this definition version took effect, stamped by approval; null on a definition not yet approved.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("effective_from_utc")]
    public DateTime? EffectiveFromUtc { get; init; }
}
