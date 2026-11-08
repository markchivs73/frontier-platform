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

    /// <summary>
    /// S13.223 (GOV-14): publishing through the lifecycle supersedes the version it replaces and
    /// records the successor, leaves the definition untouched, and keeps the draft open.
    /// </summary>
    [Fact]
    public async Task ApproveThroughLifecycle_SupersedesThePreviousVersion_AndKeepsTheDraft()
    {
        var store = _store!;
        await store.PublishVersionAsync(VersionDoc("wf-gov14-a", 1), CancellationToken.None);
        await store.SetCurrentVersionAsync("wf-gov14-a", 1, CancellationToken.None);
        var definitionBefore = await RawDefinitionAsync("wf-gov14-a", 1);
        var proposalId = await ProposeAsync(store, "wf-gov14-a");
        var compiler = new Mock<IDefinitionCompiler>().PassesValidation();
        compiler.Setup(c => c.ComputeDefinitionHash(It.IsAny<Frontier.Platform.Workflow.Model.WorkflowDefinition>())).Returns("sha256:v2");
        var embedder = new Mock<IDefinitionContractEmbedder>();
        embedder.Setup(e => e.EmbedAsync(It.IsAny<Frontier.Platform.Workflow.Model.WorkflowDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Frontier.Platform.Workflow.Model.WorkflowDefinition d, CancellationToken _) => d);
        var lifecycle = new DefinitionLifecycleService(store, compiler.Object, embedder.Object);

        await lifecycle.ApproveAsync(proposalId, "user:sarah", CancellationToken.None);

        var previous = await store.GetVersionAsync("wf-gov14-a", 1, CancellationToken.None);
        Assert.Equal("superseded", previous!.State);
        Assert.Equal(2, previous.SupersededByVersion);
        Assert.Equal("published", (await store.GetVersionAsync("wf-gov14-a", 2, CancellationToken.None))!.State);
        Assert.Equal(definitionBefore, await RawDefinitionAsync("wf-gov14-a", 1));
        Assert.NotNull(await store.GetDraftAsync("wf-gov14-a", CancellationToken.None));
    }

    /// <summary>
    /// A retired version keeps its state but records its successor, so un-retiring it returns it to
    /// <c>superseded</c>, never to a second <c>published</c>; each transition refuses a second attempt.
    /// </summary>
    [Fact]
    public async Task Supersede_RecordsTheSuccessorOnARetiredVersion_SoUnretireRestoresSuperseded()
    {
        var store = _store!;
        var retirement = new RetirementInfoDocument { RetiredAtUtc = DateTime.UtcNow, RetiredBy = "user:admin", Reason = "r" };
        await store.PublishVersionAsync(VersionDoc("wf-gov14-b", 1), CancellationToken.None);
        await store.PublishVersionAsync(VersionDoc("wf-gov14-b", 2), CancellationToken.None);
        Assert.True(await store.RetireVersionAsync("wf-gov14-b", 1, retirement, CancellationToken.None));

        Assert.True(await store.SupersedeVersionAsync("wf-gov14-b", 1, 3, CancellationToken.None));
        Assert.True(await store.SupersedeVersionAsync("wf-gov14-b", 2, 3, CancellationToken.None));
        Assert.False(await store.SupersedeVersionAsync("wf-gov14-b", 1, 4, CancellationToken.None));
        Assert.False(await store.SupersedeVersionAsync("wf-gov14-b", 2, 4, CancellationToken.None));
        Assert.False(await store.SupersedeVersionAsync("wf-gov14-b", 9, 4, CancellationToken.None));

        var retired = await store.GetVersionAsync("wf-gov14-b", 1, CancellationToken.None);
        Assert.Equal(("retired", 3), (retired!.State, retired.SupersededByVersion));
        Assert.True(await store.UnretireVersionAsync("wf-gov14-b", 1, CancellationToken.None));
        var restored = await store.GetVersionAsync("wf-gov14-b", 1, CancellationToken.None);
        Assert.Equal(("superseded", 3), (restored!.State, restored.SupersededByVersion));
        Assert.Null(restored.Retirement);
        var second = await store.GetVersionAsync("wf-gov14-b", 2, CancellationToken.None);
        Assert.Equal(("superseded", 3), (second!.State, second.SupersededByVersion));
    }

    private static async Task<string> ProposeAsync(CosmosDefinitionStore store, string workflowId)
    {
        var lifecycle = new DefinitionLifecycleService(store, Mock.Of<IDefinitionCompiler>(), Mock.Of<IDefinitionContractEmbedder>());
        var draft = await lifecycle.CreateDraftAsync(workflowId, 1, "user:mark", CancellationToken.None);
        var report = new ValidationReport(
            WorkflowId: workflowId,
            DraftRevision: draft.DraftRevision,
            ValidatedAtUtc: DateTime.UtcNow,
            Outcome: ValidationOutcome.Pass,
            Findings: [],
            ResourceVersions: new Dictionary<string, string>());
        var proposal = await lifecycle.ProposePublishAsync(workflowId, draft.DraftRevision, report, "user:mark", CancellationToken.None);
        return proposal.ProposalId;
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
