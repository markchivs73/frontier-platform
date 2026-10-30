using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using static Frontier.Platform.ModelRoleConfig.Tests.RoleDefinitionSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests.Integration;

/// <summary>
/// ADR-PA40 emulator tests: <see cref="CosmosRoleRegistry.GetCatalogueAsync"/> reads every role's
/// current stored definition across role partitions with no fallback to the code constant, and the
/// definition documents leave the mapping-version queries of <see cref="CosmosMappingProposalStore"/>
/// unchanged. Provisions its own database, so it does not depend on <c>cosmos-init.py</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosRoleRegistryDefinitionIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string DatabaseId = "frontier-role-definition-tests";

    private CosmosClient client = null!;
    private Container container = null!;
    private CosmosRoleRegistry registry = null!;
    private CosmosMappingProposalStore store = null!;

    public async Task InitializeAsync()
    {
        client = new CosmosClient(Frontier.TestSupport.EmulatorCosmos.Endpoint, Frontier.TestSupport.EmulatorCosmos.Key, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            UseSystemTextJsonSerializerWithOptions = CanonicalProfile.Options,
        });

        var database = await client.CreateDatabaseIfNotExistsAsync(DatabaseId);
        var response = await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(CosmosRoleRegistry.ContainerName, "/role_id") { DefaultTimeToLive = -1 });
        container = response.Container;

        var options = Options.Create(new CosmosOptions { Database = DatabaseId });
        registry = new CosmosRoleRegistry(client, options);
        store = new CosmosMappingProposalStore(client, options);
    }

    public async Task DisposeAsync() => await client.GetDatabase(DatabaseId).DeleteAsync();

    public void Dispose() => client.Dispose();

    [Fact]
    public async Task GetCatalogueAsync_NothingStored_IsEmpty_WithNoFallbackToTheConstant()
    {
        await SeedMappingAsync(Phase1RoleCatalogue.DeepReasoningMappingV1);

        var catalogue = await registry.GetCatalogueAsync(CancellationToken.None);

        Assert.Empty(catalogue.Roles);
    }

    [Fact]
    public async Task GetCatalogueAsync_ReturnsEveryRolesCurrentDefinition_AcrossPartitions()
    {
        foreach (var role in Phase1.Roles)
        {
            await SeedDefinitionAsync(Migrated(role));
        }

        await SeedDefinitionAsync(AgentBacked() with { DefinitionVersion = 1, Description = "superseded" }, moveCurrent: false);
        await SeedDefinitionAsync(AgentBacked());

        var catalogue = await registry.GetCatalogueAsync(CancellationToken.None);

        Assert.Equal(["deep-reasoning", "embeddings", "fast", "gen-record-pass", "structured-extraction"], catalogue.Roles.Select(role => role.RoleId));
        Assert.Equal(AgentBacked(), catalogue.Roles.Single(role => role.RoleId == "gen-record-pass"));
        Assert.All(Phase1.Roles, role => Assert.Contains(Migrated(role), catalogue.Roles));
    }

    [Fact]
    public async Task MappingHistory_IsUnchangedByDefinitionDocumentsInTheSamePartition()
    {
        var mapping = Phase1RoleCatalogue.DeepReasoningMappingV1;
        await SeedMappingAsync(mapping);
        await SeedDefinitionAsync(Migrated(Phase1Role(mapping.RoleId)));
        await SeedDefinitionAsync(Migrated(Phase1Role(mapping.RoleId)) with { DefinitionVersion = 2 });

        Assert.Equal([1], await store.ListMappingVersionsAsync(mapping.RoleId, CancellationToken.None));
        Assert.Equal(mapping.Chain, Assert.Single(await store.ListMappingsAsync(mapping.RoleId, CancellationToken.None)).Chain);
        Assert.Equal(1, await store.FindCurrentVersionAsync(mapping.RoleId, CancellationToken.None));
        Assert.Equal(1, (await registry.GetActiveMappingAsync(mapping.RoleId, CancellationToken.None)).MappingVersion);
    }

    private async Task SeedDefinitionAsync(RoleDefinition definition, bool moveCurrent = true)
    {
        var document = RoleDefinitionDocument.FromDomain(definition);
        await container.UpsertItemAsync(document, new PartitionKey(document.RoleId));
        if (moveCurrent)
        {
            await container.UpsertItemAsync(RoleDefinitionCurrentDocument.For(document), new PartitionKey(document.RoleId));
        }
    }

    private async Task SeedMappingAsync(RoleMapping mapping)
    {
        await container.UpsertItemAsync(RoleMappingDocument.FromDomain(mapping), new PartitionKey(mapping.RoleId));
        await container.UpsertItemAsync(RoleMappingCurrentDocument.FromDomain(mapping), new PartitionKey(mapping.RoleId));
    }
}
