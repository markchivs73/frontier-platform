namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Consumer-owned port (ADR-PA2, S11.4): supplies the distinct role ids referenced by the
/// consuming solution's published workflow definitions, for <see cref="RoleCatalogueCheck"/>
/// to verify against the active role catalogue. The platform library owns the invariant
/// ("every referenced role has an active fleet/canary mapping"); the solution owns how
/// referenced roles are discovered — this repo's Host adapts its published-definition
/// store; another solution supplies whatever its definition storage is.
/// </summary>
public interface IReferencedRolesSource
{
    /// <summary>The distinct role ids referenced by currently-published workflow definitions.</summary>
    Task<IReadOnlySet<string>> GetReferencedRoleIdsAsync(CancellationToken cancellationToken);
}
