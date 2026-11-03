#pragma warning disable CA2000  // HttpClient/Handler disposal: CosmosClientOptions takes ownership
using System.Net.Http;
using System.Text.Json.Nodes;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.TestSupport;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// GOV-01: retiring and un-retiring a published version must persist. Before the fix both went
/// through <see cref="CosmosDefinitionStore.PublishVersionAsync"/>, which forces <c>published</c>
/// and returns an existing version unchanged, so nothing was stored. Runs against the Cosmos
/// emulator on its own database so it never touches another suite's state.
/// </summary>
[Trait("Category", "Integration")]
public sealed class VersionRetirementIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string DatabaseId = "frontier-platform-gov01-retire-tests";

    private CosmosClient? _client;
    private Database? _database;
    private Container? _container;
    private CosmosDefinitionStore? _store;

    public async Task InitializeAsync()
    {
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        var httpClient = new HttpClient(handler);
        _client = new CosmosClient(EmulatorCosmos.Endpoint, EmulatorCosmos.Key, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            HttpClientFactory = () => httpClient,
            UseSystemTextJsonSerializerWithOptions = Frontier.Platform.Serialization.CanonicalProfile.Options,
        });
        _database = await _client.CreateDatabaseIfNotExistsAsync(DatabaseId);
        _container = await _database.CreateContainerIfNotExistsAsync(
            new ContainerProperties { Id = "workflow-definitions", PartitionKeyPath = "/workflowId" });
        _store = new CosmosDefinitionStore(_container);
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DeleteAsync();
        _client?.Dispose();
    }

    public void Dispose() => _client?.Dispose();

    [Fact]
    public async Task RetireThroughLifecycle_PersistsStateAndRetirement_AndLeavesDefinitionUntouched()
    {
        var store = _store!;
        await store.PublishVersionAsync(VersionDoc("wf-gov01-a", 1), CancellationToken.None);
        var definitionBefore = await RawDefinitionAsync("wf-gov01-a", 1);
        var lifecycle = new DefinitionLifecycleService(store, Mock.Of<IDefinitionCompiler>(), Mock.Of<IDefinitionContractEmbedder>());

        await lifecycle.RetireAsync("wf-gov01-a", 1, "user:admin", "End of life", CancellationToken.None);

        var stored = await store.GetVersionAsync("wf-gov01-a", 1, CancellationToken.None);
        Assert.Equal("retired", stored!.State);
        Assert.Equal("user:admin", stored.Retirement!.RetiredBy);
        Assert.Equal("End of life", stored.Retirement.Reason);
        Assert.DoesNotContain(await store.ListPublishedVersionsAsync(CancellationToken.None), v => v.WorkflowId == "wf-gov01-a");
        Assert.Equal(definitionBefore, await RawDefinitionAsync("wf-gov01-a", 1));
    }

    [Fact]
    public async Task UnretireThroughLifecycle_RestoresPublished_AndRemovesRetirement()
    {
        var store = _store!;
        await store.PublishVersionAsync(VersionDoc("wf-gov01-b", 2), CancellationToken.None);
        var lifecycle = new DefinitionLifecycleService(store, Mock.Of<IDefinitionCompiler>(), Mock.Of<IDefinitionContractEmbedder>());
        await lifecycle.RetireAsync("wf-gov01-b", 2, "user:admin", "Paused", CancellationToken.None);

        await lifecycle.UnretireAsync("wf-gov01-b", 2, "user:admin", "Restored", CancellationToken.None);

        var stored = await store.GetVersionAsync("wf-gov01-b", 2, CancellationToken.None);
        Assert.Equal("published", stored!.State);
        Assert.Null(stored.Retirement);
        var raw = await _container!.ReadItemAsync<JsonObject>("wf-gov01-b:v2", new PartitionKey("wf-gov01-b"));
        Assert.False(raw.Resource.ContainsKey("retirement"));
    }

    [Fact]
    public async Task Transitions_RefuseTheWrongStartingState_AndAMissingVersion()
    {
        var store = _store!;
        var retirement = new RetirementInfoDocument { RetiredAtUtc = DateTime.UtcNow, RetiredBy = "user:admin", Reason = "r" };
        await store.PublishVersionAsync(VersionDoc("wf-gov01-c", 1), CancellationToken.None);

        Assert.False(await store.UnretireVersionAsync("wf-gov01-c", 1, CancellationToken.None));
        Assert.True(await store.RetireVersionAsync("wf-gov01-c", 1, retirement, CancellationToken.None));
        Assert.False(await store.RetireVersionAsync("wf-gov01-c", 1, retirement, CancellationToken.None));
        Assert.False(await store.RetireVersionAsync("wf-gov01-c", 9, retirement, CancellationToken.None));
        Assert.False(await store.UnretireVersionAsync("wf-gov01-c", 9, CancellationToken.None));
    }

    private async Task<string> RawDefinitionAsync(string workflowId, int version)
    {
        var raw = await _container!.ReadItemAsync<JsonObject>($"{workflowId}:v{version}", new PartitionKey(workflowId));
        return raw.Resource["definition"]!.ToJsonString();
    }

    private static DefinitionVersionDocument VersionDoc(string workflowId, int version) => new()
    {
        Id = $"{workflowId}:v{version}",
        WorkflowId = workflowId,
        State = "published",
        DefinitionVersion = version,
        DefinitionHash = "sha256:abc",
        Definition = WorkflowDefinitionFixture.MinimalDefinition(),
        ProposedBy = "user:mark",
        ApprovedBy = "user:sarah",
        ProposedUtc = DateTime.UtcNow,
        ApprovedUtc = DateTime.UtcNow,
        ValidationReportRef = "ref",
    };
}
