namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The full set of declared roles (doc 08 §3 <c>IRoleRegistry.GetCatalogueAsync</c>):
/// the universe of values an <c>AgentTaskNode.Role</c> may legally reference.
/// </summary>
public sealed record RoleCatalogue
{
    /// <summary>Every declared role, in catalogue order.</summary>
    public required IReadOnlyList<RoleDefinition> Roles { get; init; }
}
