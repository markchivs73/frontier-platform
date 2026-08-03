using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ContextAssembly.Tests.Integration;

/// <summary>
/// Tests for <see cref="CosmosEngagementContextStore"/> (S6.2a): Cosmos-backed
/// engagement context with epoch-based versioning. Uses the emulator (via Aspire AppHost).
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosEngagementContextStoreTests : IAsyncLifetime, IDisposable
{
    private CosmosClient cosmosClient = null!;
    private Container container = null!;

    private const string DatabaseId = "frontier-context-store-tests";
    private const string ContainerId = "engagement-context";
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        cosmosClient = new CosmosClient(Frontier.TestSupport.EmulatorCosmos.Endpoint, EmulatorKey, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                CheckCertificateRevocationList = true,
            }),
            // The store's documents carry [JsonPropertyName] wire names (snake_case, lowercase
            // "id"); without the canonical STJ serializer the SDK's default Newtonsoft path
            // ignores those attributes and writes PascalCase - Cosmos then rejects the document
            // ("Document does not contain an id field"). Mirrors the production client wiring.
            UseSystemTextJsonSerializerWithOptions = Frontier.Platform.Serialization.CanonicalProfile.Options,
        });

        var database = await cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseId);
        var containerResponse = await database.Database.CreateContainerIfNotExistsAsync(
            ContainerId,
            partitionKeyPath: "/engagement_id");
        container = containerResponse.Container;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        try
        {
            await cosmosClient.GetDatabase(DatabaseId).DeleteAsync();
        }
        catch (CosmosException)
        {
            // Database may not exist if tests failed early
        }
        cosmosClient.Dispose();
    }

    /// <inheritdoc />
    /// <remarks>xUnit invokes BOTH <see cref="IAsyncLifetime.DisposeAsync"/> and this method;
    /// re-running the async teardown here double-deletes the database and then touches the
    /// already-disposed client (ObjectDisposedException). Dispose the client only.</remarks>
    public void Dispose() => cosmosClient.Dispose();

    [Fact]
    public async Task GetDynamicContextAsync_ReturnsNullForNonExistentEngagement()
    {
        var store = new CosmosEngagementContextStore(container);

        var result = await store.GetDynamicContextAsync("eng-nonexistent", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertDynamicContextAsync_CreatesEpochAndPointer()
    {
        var store = new CosmosEngagementContextStore(container);
        var engagementId = "eng-1";
        var content = """{"data":"test"}""";

        await store.UpsertDynamicContextAsync(engagementId, content, CancellationToken.None);

        var retrieved = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        Assert.Equal(content, retrieved);
    }

    [Fact]
    public async Task UpsertDynamicContextAsync_IncrementsEpochOnMultipleWrites()
    {
        var store = new CosmosEngagementContextStore(container);
        var engagementId = "eng-2";
        var content1 = """{"v":1}""";
        var content2 = """{"v":2}""";

        await store.UpsertDynamicContextAsync(engagementId, content1, CancellationToken.None);
        var retrieved1 = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        Assert.Equal(content1, retrieved1);

        await store.UpsertDynamicContextAsync(engagementId, content2, CancellationToken.None);
        var retrieved2 = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        Assert.Equal(content2, retrieved2);
    }

    [Fact]
    public async Task UpsertDynamicContextAsync_CanRetrievePriorEpochs()
    {
        var store = new CosmosEngagementContextStore(container);
        var engagementId = "eng-3";
        var content1 = """{"epoch":1}""";
        var content2 = """{"epoch":2}""";

        // Write epoch 0
        await store.UpsertDynamicContextAsync(engagementId, content1, CancellationToken.None);

        // Write epoch 1
        await store.UpsertDynamicContextAsync(engagementId, content2, CancellationToken.None);

        // Current should be epoch 1
        var current = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        Assert.Equal(content2, current);

        // Both epoch documents should exist in Cosmos
        var epoch0Id = $"{engagementId}:ctx:e000000";
        var epoch0Doc = await container.ReadItemAsync<EngagementContextEpoch>(
            epoch0Id,
            new PartitionKey(engagementId));
        Assert.NotNull(epoch0Doc.Resource);
        Assert.Equal(content1, epoch0Doc.Resource.Content);
    }

    [Fact]
    public async Task UpsertDynamicContextAsync_ComputesContentHashCorrectly()
    {
        var store = new CosmosEngagementContextStore(container);
        var engagementId = "eng-4";
        var content = """{"test":"data"}""";

        await store.UpsertDynamicContextAsync(engagementId, content, CancellationToken.None);

        var pointerId = $"{engagementId}:ctx:current";
        var pointerDoc = await container.ReadItemAsync<EngagementContextPointer>(
            pointerId,
            new PartitionKey(engagementId));

        var expectedHash = CanonicalProfile.Hash(content);
        Assert.Equal(expectedHash, pointerDoc.Resource!.ContentHash);
    }
}
