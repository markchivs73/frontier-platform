using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Frontier.Platform.ContextAssembly.Tests.Integration;

/// <summary>
/// Integration tests for S6.2b IDynamicContextRefresher against the Cosmos emulator.
/// Uses CosmosEngagementContextStore to verify epoch-based versioning and pointer updates.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DynamicContextRefresherIntegrationTests : IAsyncLifetime, IDisposable
{
    private CosmosClient cosmosClient = null!;
    private Container container = null!;

    private const string DatabaseId = "frontier-context-refresher-tests";
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
    public async Task RefreshDynamicAsync_WritesEpochAndPointer()
    {
        var store = new CosmosEngagementContextStore(container);
        var logger = new TestLogger();
        using var refresher = new DynamicContextRefresher(store, logger);
        var engagementId = new EngagementId("eng-integration-1");
        var content = """{"test":"data"}""";

        // First refresh (empty store, should create epoch 0 and pointer)
        var result1 = await refresher.RefreshDynamicAsync(engagementId, content, "initial", CancellationToken.None);

        Assert.True(result1.Refreshed);
        Assert.Equal(0, result1.Epoch);
        Assert.Equal(CanonicalProfile.Hash(content), result1.ContentHash);

        // Verify pointer document exists and points to epoch 0
        var pointerId = $"{engagementId}:ctx:current";
        var pointerDoc = await container.ReadItemAsync<EngagementContextPointer>(
            pointerId,
            new PartitionKey(engagementId));
        Assert.NotNull(pointerDoc.Resource);
        Assert.Equal(0, pointerDoc.Resource.Epoch);
        Assert.Equal(result1.ContentHash, pointerDoc.Resource.ContentHash);

        // Verify epoch 0 document exists
        var epochId = $"{engagementId}:ctx:e000000";
        var epochDoc = await container.ReadItemAsync<EngagementContextEpoch>(
            epochId,
            new PartitionKey(engagementId));
        Assert.NotNull(epochDoc.Resource);
        Assert.Equal(content, epochDoc.Resource.Content);
    }

    [Fact]
    public async Task RefreshDynamicAsync_IncrementEpochOnChange()
    {
        var store = new CosmosEngagementContextStore(container);
        var logger = new TestLogger();
        using var refresher = new DynamicContextRefresher(store, logger);
        var engagementId = new EngagementId("eng-integration-2");
        var content1 = """{"v":1}""";
        var content2 = """{"v":2}""";

        // First refresh
        var result1 = await refresher.RefreshDynamicAsync(engagementId, content1, "first", CancellationToken.None);
        Assert.Equal(0, result1.Epoch);

        // Second refresh with different content
        var result2 = await refresher.RefreshDynamicAsync(engagementId, content2, "second", CancellationToken.None);
        Assert.True(result2.Refreshed);
        Assert.Equal(1, result2.Epoch);

        // Verify pointer updated
        var pointerId = $"{engagementId}:ctx:current";
        var pointerDoc = await container.ReadItemAsync<EngagementContextPointer>(
            pointerId,
            new PartitionKey(engagementId));
        Assert.Equal(1, pointerDoc.Resource!.Epoch);

        // Verify both epochs exist
        var epoch0Id = $"{engagementId}:ctx:e000000";
        var epoch0 = await container.ReadItemAsync<EngagementContextEpoch>(epoch0Id, new PartitionKey(engagementId));
        Assert.Equal(content1, epoch0.Resource!.Content);

        var epoch1Id = $"{engagementId}:ctx:e000001";
        var epoch1 = await container.ReadItemAsync<EngagementContextEpoch>(epoch1Id, new PartitionKey(engagementId));
        Assert.Equal(content2, epoch1.Resource!.Content);
    }

    [Fact]
    public async Task RefreshDynamicAsync_NoEpochBumpOnIdenticalContent()
    {
        var store = new CosmosEngagementContextStore(container);
        var logger = new TestLogger();
        using var refresher = new DynamicContextRefresher(store, logger);
        var engagementId = new EngagementId("eng-integration-3");
        var content = """{"stable":"data"}""";

        // First refresh
        var result1 = await refresher.RefreshDynamicAsync(engagementId, content, "first", CancellationToken.None);
        Assert.True(result1.Refreshed);
        Assert.Equal(0, result1.Epoch);

        // Second refresh with same content
        var result2 = await refresher.RefreshDynamicAsync(engagementId, content, "second", CancellationToken.None);
        Assert.False(result2.Refreshed);
        Assert.Equal(0, result2.Epoch); // Epoch did not change

        // Verify only epoch 0 exists (no epoch 1)
        var epochId = $"{engagementId}:ctx:e000001";
        var ex = await Assert.ThrowsAsync<CosmosException>(async () =>
            await container.ReadItemAsync<EngagementContextEpoch>(epochId, new PartitionKey(engagementId)));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
    }

    /// <summary>
    /// Minimal ILogger for testing.
    /// </summary>
    private sealed class TestLogger : ILogger<DynamicContextRefresher>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
