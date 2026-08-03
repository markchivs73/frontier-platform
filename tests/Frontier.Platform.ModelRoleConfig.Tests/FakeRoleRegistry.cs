namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// In-memory <see cref="IRoleRegistry"/> seeded with an ordered list of
/// <see cref="RoleMapping"/> instances (for <see cref="ModelResolver"/> and governance
/// tests). The first mapping in the list is returned by
/// <see cref="GetActiveMappingAsync"/>; <see cref="GetMappingVersionAsync"/> looks up
/// by <see cref="RoleMapping.MappingVersion"/>.
/// </summary>
internal sealed class FakeRoleRegistry(params RoleMapping[] mappings) : IRoleRegistry
{
    /// <inheritdoc />
    public Task<RoleCatalogue> GetCatalogueAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Phase1RoleCatalogue.Catalogue);

    /// <inheritdoc />
    public Task<RoleMapping> GetActiveMappingAsync(string roleId, CancellationToken cancellationToken) =>
        Task.FromResult(mappings[0]);

    /// <inheritdoc />
    public Task<RoleMapping> GetMappingVersionAsync(string roleId, int version, CancellationToken cancellationToken) =>
        Task.FromResult(mappings.Single(m => m.MappingVersion == version));
}
