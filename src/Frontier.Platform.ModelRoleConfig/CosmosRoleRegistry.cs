using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// <see cref="IRoleRegistry"/> over the <c>model-role-config</c> container (doc 08 §6): the
/// catalogue is every role's current stored definition (ADR-PA40), and a mapping is a point-read of
/// the <c>current</c> pointer then its referenced version, or a specific historical version directly.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 08 §6); exercised by integration tests against the Cosmos emulator, not the unit-coverage gate.")]
internal sealed class CosmosRoleRegistry(CosmosClient client, IOptions<CosmosOptions> options) : IRoleRegistry, IRoleMappingWriter
{
    /// <summary>The container holding <see cref="RoleMappingDocument"/>s and <see cref="RoleMappingCurrentDocument"/>s (doc 08 §6).</summary>
    internal const string ContainerName = "model-role-config";

    /// <inheritdoc />
    public async Task<RoleCatalogue> GetCatalogueAsync(CancellationToken cancellationToken)
    {
        // Two queries across role partitions, uncached (ADR-PA40): the pointers, then the versions
        // they name. No fallback to Phase1RoleCatalogue — an unseeded store is an empty catalogue,
        // and RoleCatalogueCheck fails the boot for any role a published definition references.
        var container = client.GetContainer(options.Value.Database, ContainerName);
        var pointers = await QueryAsync<RoleDefinitionCurrentDocument>(
            container,
            new QueryDefinition("SELECT * FROM c WHERE c.doc_type = @docType").WithParameter("@docType", RoleDefinitionCurrentDocument.CurrentDocType),
            cancellationToken);
        var versions = pointers.Count == 0
            ? []
            : await QueryAsync<RoleDefinitionDocument>(
                container,
                new QueryDefinition("SELECT * FROM c WHERE c.doc_type = @docType AND ARRAY_CONTAINS(@ids, c.id)")
                    .WithParameter("@docType", RoleDefinitionDocument.DefinitionDocType)
                    .WithParameter("@ids", pointers.Select(pointer => pointer.CurrentRef).ToArray()),
                cancellationToken);

        return StoredRoleCatalogue.Assemble(pointers, versions);
    }

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

    private static async Task<List<T>> QueryAsync<T>(Container container, QueryDefinition query, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        using var iterator = container.GetItemQueryIterator<T>(query);
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return results;
    }
}
