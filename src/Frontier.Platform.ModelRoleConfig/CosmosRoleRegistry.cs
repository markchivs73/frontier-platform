using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// <see cref="IRoleRegistry"/> over the frozen Phase 1 catalogue
/// (<see cref="Phase1RoleCatalogue"/>, C-3) and the <c>model-role-config</c> container
/// (doc 08 §6): point-reads the <c>current</c> pointer then its referenced mapping
/// version, or a specific historical version directly.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 08 §6); exercised by integration tests against the Cosmos emulator, not the unit-coverage gate.")]
internal sealed class CosmosRoleRegistry(CosmosClient client, IOptions<CosmosOptions> options) : IRoleRegistry, IRoleMappingWriter
{
    /// <summary>The container holding <see cref="RoleMappingDocument"/>s and <see cref="RoleMappingCurrentDocument"/>s (doc 08 §6).</summary>
    internal const string ContainerName = "model-role-config";

    /// <inheritdoc />
    public Task<RoleCatalogue> GetCatalogueAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Phase1RoleCatalogue.Catalogue);

    /// <inheritdoc />
    public async Task<RoleMapping> GetActiveMappingAsync(string roleId, CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Value.Database, ContainerName);
        var partitionKey = new PartitionKey(roleId);

        var pointer = await container.ReadItemAsync<RoleMappingCurrentDocument>(
            ModelRoleConfigDocumentId.ForCurrent(roleId), partitionKey, cancellationToken: cancellationToken);

        return await GetMappingVersionAsync(roleId, pointer.Resource.MappingVersion, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RoleMapping> GetMappingVersionAsync(string roleId, int version, CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Value.Database, ContainerName);
        var partitionKey = new PartitionKey(roleId);

        var document = await container.ReadItemAsync<RoleMappingDocument>(
            ModelRoleConfigDocumentId.ForVersion(roleId, version), partitionKey, cancellationToken: cancellationToken);

        return document.Resource.ToDomain();
    }

    /// <inheritdoc />
    public async Task WriteCurrentAsync(string roleId, int toVersion, CancellationToken ct)
    {
        var container = client.GetContainer(options.Value.Database, ContainerName);
        var pointer = new RoleMappingCurrentDocument
        {
            Id = ModelRoleConfigDocumentId.ForCurrent(roleId),
            RoleId = roleId,
            CurrentRef = ModelRoleConfigDocumentId.ForVersion(roleId, toVersion),
            MappingVersion = toVersion,
        };
        await container.UpsertItemAsync(pointer, new PartitionKey(roleId), cancellationToken: ct);
    }
}
