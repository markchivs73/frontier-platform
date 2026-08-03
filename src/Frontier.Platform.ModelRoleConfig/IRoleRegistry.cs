namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The role catalogue and role→mapping store (doc 08 §3). The catalogue
/// (<see cref="GetCatalogueAsync"/>) is the frozen Phase 1 set
/// (<see cref="Phase1RoleCatalogue"/>, C-3); mappings
/// (<see cref="GetActiveMappingAsync"/>, <see cref="GetMappingVersionAsync"/>) are
/// versioned documents in the <c>model-role-config</c> container (doc 08 §6).
/// </summary>
public interface IRoleRegistry
{
    /// <summary>All declared roles and their capability requirements.</summary>
    Task<RoleCatalogue> GetCatalogueAsync(CancellationToken cancellationToken);

    /// <summary>The mapping currently active for <paramref name="roleId"/> (the <c>current</c> pointer, doc 08 §6).</summary>
    Task<RoleMapping> GetActiveMappingAsync(string roleId, CancellationToken cancellationToken);

    /// <summary>A specific historical mapping version — used to resolve under an execution's pinned mapping (doc 08 §5).</summary>
    Task<RoleMapping> GetMappingVersionAsync(string roleId, int version, CancellationToken cancellationToken);
}
