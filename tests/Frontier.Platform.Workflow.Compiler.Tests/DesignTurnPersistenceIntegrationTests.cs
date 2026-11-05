using System.Net.Http;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.TestSupport;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.224 (review-01 GOV-11): a design turn's validated mentions, with their <c>resolved</c> flags,
/// survive a round trip through the emulator — the chat history reload reads them back (doc 14 §8a).
/// Runs on its own database so it never touches another suite's state.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DesignTurnPersistenceIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string DatabaseId = "frontier-platform-s13224-design-turn-tests";

    private HttpClientHandler? _handler;
    private HttpClient? _httpClient;
    private CosmosClient? _client;
    private Database? _database;
    private CosmosDefinitionStore? _store;

    public async Task InitializeAsync()
    {
        _handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        _httpClient = new HttpClient(_handler);
        var httpClient = _httpClient;
        _client = new CosmosClient(EmulatorCosmos.Endpoint, EmulatorCosmos.Key, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            HttpClientFactory = () => httpClient,
            UseSystemTextJsonSerializerWithOptions = Frontier.Platform.Serialization.CanonicalProfile.Options,
        });
        _database = await _client.CreateDatabaseIfNotExistsAsync(DatabaseId);
        Container container = await _database.CreateContainerIfNotExistsAsync(
            new ContainerProperties { Id = "workflow-definitions", PartitionKeyPath = "/workflowId" });
        _store = new CosmosDefinitionStore(container);
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DeleteAsync();
        Dispose();
    }

    /// <summary>Disposes the Cosmos client and the emulator HTTP client.</summary>
    public void Dispose()
    {
        _client?.Dispose();
        _httpClient?.Dispose();
        _handler?.Dispose();
    }

    [Fact]
    public async Task PersistDesignTurn_KeepsMentionsAndTheirResolvedFlags_WhenTheTurnIsReloaded()
    {
        var turn = DesignTurnFixture.WithMentions("wf-s13224");

        await _store!.PersistDesignTurnAsync(turn, CancellationToken.None);

        var reloaded = await _store.GetDesignTurnAsync(turn.Id, CancellationToken.None);
        var history = await _store.GetAllDesignTurnsAsync(turn.WorkflowId, CancellationToken.None);
        Assert.Equal(turn.Mentions, reloaded!.Mentions);
        Assert.Equal(turn.Mentions, Assert.Single(history).Mentions);
    }
}
