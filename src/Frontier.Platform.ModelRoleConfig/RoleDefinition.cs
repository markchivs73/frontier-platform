namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// A catalogue entry describing what a role is for and what it needs (doc 08 §4):
/// agents and workflow nodes declare a <see cref="RoleId"/>, never a model ID (doc 08 §2
/// principle 1 — total indirection). The Phase 1 catalogue is frozen by
/// <see cref="Phase1RoleCatalogue"/> (S4.3/C-3).
/// </summary>
public sealed record RoleDefinition
{
    /// <summary>The role identifier (e.g. <c>"deep-reasoning"</c>) — the only thing a <c>AgentTaskNode.Role</c> may reference.</summary>
    public required string RoleId { get; init; }

    /// <summary>Human-readable description of what this role is for and which agents use it.</summary>
    public required string Description { get; init; }

    /// <summary>What's commercially at stake if this role's output is wrong — drives mapping governance posture.</summary>
    public required StakesLevel Stakes { get; init; }

    /// <summary>The capability profile any model in this role's mapping chain must satisfy.</summary>
    public required CapabilityRequirements Requirements { get; init; }
}
