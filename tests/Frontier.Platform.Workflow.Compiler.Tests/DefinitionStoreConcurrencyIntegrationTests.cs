#pragma warning disable CA2000  // HttpClient/Handler disposal: CosmosClientOptions takes ownership
using System.Net.Http;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.TestSupport;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// GOV-03 and GOV-04: a draft save and a proposal decision must not overwrite a write that landed
/// after they read. Each race is staged deterministically: the store under test talks to the
/// emulator through a <see cref="Container"/> that lets a competing writer land just before the
/// store's own write. Runs on its own database so it never touches another suite's state.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DefinitionStoreConcurrencyIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string DatabaseId = "frontier-platform-gov03-concurrency-tests";

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
    public async Task SaveDraft_WhenAnotherSaveOnTheSameRevisionLandsFirst_ReturnsConflictAndKeepsTheWinner()
    {
        await _store!.CreateDraftAsync("wf-gov03-a", 0, Draft("wf-gov03-a", "rev-0"), CancellationToken.None);
        var racing = new CosmosDefinitionStore(Interleaving(
            () => _store.SaveDraftAsync("wf-gov03-a", Draft("wf-gov03-a", "rev-winner"), "rev-0", CancellationToken.None)));

        var result = await racing.SaveDraftAsync("wf-gov03-a", Draft("wf-gov03-a", "rev-loser"), "rev-0", CancellationToken.None);

        var conflict = Assert.IsType<SaveDraftResultConflict>(result);
        Assert.Equal("rev-winner", conflict.CurrentRevision);
        Assert.Equal("rev-winner", (await _store.GetDraftAsync("wf-gov03-a", CancellationToken.None))!.DraftRevision);
    }

    [Fact]
    public async Task SaveDraft_WithNoRace_ReplacesTheDraft_AndAStaleRevisionStillConflicts()
    {
        await _store!.CreateDraftAsync("wf-gov03-b", 0, Draft("wf-gov03-b", "rev-0"), CancellationToken.None);

        var saved = await _store.SaveDraftAsync("wf-gov03-b", Draft("wf-gov03-b", "rev-1"), "rev-0", CancellationToken.None);
        var stale = await _store.SaveDraftAsync("wf-gov03-b", Draft("wf-gov03-b", "rev-2"), "rev-0", CancellationToken.None);

        Assert.Equal("rev-1", Assert.IsType<SaveDraftResultSuccess>(saved).Draft.DraftRevision);
        Assert.Equal("rev-1", Assert.IsType<SaveDraftResultConflict>(stale).CurrentRevision);
    }

    [Fact]
    public async Task SaveDraft_WithNoDraftYet_CreatesIt()
    {
        var saved = await _store!.SaveDraftAsync("wf-gov03-c", Draft("wf-gov03-c", "rev-1"), "rev-0", CancellationToken.None);

        Assert.IsType<SaveDraftResultSuccess>(saved);
        Assert.Equal("rev-1", (await _store.GetDraftAsync("wf-gov03-c", CancellationToken.None))!.DraftRevision);
    }

    [Fact]
    public async Task Approve_WhenARejectLandsFirst_ReturnsFalseAndKeepsTheRejection()
    {
        await _store!.CreateProposalAsync(Proposal("wf-gov04-a", "prop-gov04-a"), CancellationToken.None);
        var racing = new CosmosDefinitionStore(Interleaving(
            () => _store.RejectProposalAsync("prop-gov04-a", "Not ready", CancellationToken.None)));

        Assert.False(await racing.ApproveProposalAsync("prop-gov04-a", CancellationToken.None));

        var stored = await _store.GetProposalAsync("prop-gov04-a", CancellationToken.None);
        Assert.Equal(ProposalState.Rejected, stored!.State);
        Assert.Equal("Not ready", stored.ApproverNoteOrReason);
    }

    [Fact]
    public async Task Reject_WhenAnApproveLandsFirst_ReturnsFalseAndKeepsTheApproval()
    {
        await _store!.CreateProposalAsync(Proposal("wf-gov04-b", "prop-gov04-b"), CancellationToken.None);
        var racing = new CosmosDefinitionStore(Interleaving(
            () => _store.ApproveProposalAsync("prop-gov04-b", CancellationToken.None)));

        Assert.False(await racing.RejectProposalAsync("prop-gov04-b", "Too late", CancellationToken.None));

        var stored = await _store.GetProposalAsync("prop-gov04-b", CancellationToken.None);
        Assert.Equal(ProposalState.Approved, stored!.State);
        Assert.Null(stored.ApproverNoteOrReason);
    }

    [Fact]
    public async Task ProposalDecisions_WithNoRace_Apply_AndRefuseAMissingOrDecidedProposal()
    {
        await _store!.CreateProposalAsync(Proposal("wf-gov04-c", "prop-gov04-c"), CancellationToken.None);

        Assert.True(await _store.RejectProposalAsync("prop-gov04-c", "No", CancellationToken.None));
        Assert.False(await _store.ApproveProposalAsync("prop-gov04-c", CancellationToken.None));
        Assert.False(await _store.ApproveProposalAsync("prop-gov04-missing", CancellationToken.None));
        Assert.Equal(ProposalState.Rejected, (await _store.GetProposalAsync("prop-gov04-c", CancellationToken.None))!.State);
    }

    /// <summary>
    /// A container over the emulator one whose first replace or upsert runs <paramref name="competingWrite"/>
    /// before its own write — another writer landing between the store's read and its write.
    /// </summary>
    private Container Interleaving(Func<Task> competingWrite)
    {
        var real = _container!;
        var raced = false;
        async Task RaceOnceAsync()
        {
            if (raced) return;
            raced = true;
            await competingWrite();
        }

        var mock = new Mock<Container>(MockBehavior.Strict);
        mock.Setup(c => c.ReadItemStreamAsync(It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns((string id, PartitionKey pk, ItemRequestOptions o, CancellationToken ct) => real.ReadItemStreamAsync(id, pk, o, ct));
        mock.Setup(c => c.ReadItemAsync<PublishProposalDocument>(It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns((string id, PartitionKey pk, ItemRequestOptions o, CancellationToken ct) => real.ReadItemAsync<PublishProposalDocument>(id, pk, o, ct));
        mock.Setup(c => c.GetItemQueryIterator<PublishProposalDocument>(It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns((QueryDefinition q, string t, QueryRequestOptions o) => real.GetItemQueryIterator<PublishProposalDocument>(q, t, o));
        SetupRacedWrites<DefinitionDraftDocument>(mock, real, RaceOnceAsync);
        SetupRacedWrites<PublishProposalDocument>(mock, real, RaceOnceAsync);
        return mock.Object;
    }

    private static void SetupRacedWrites<T>(Mock<Container> mock, Container real, Func<Task> raceOnce)
    {
        mock.Setup(c => c.ReplaceItemAsync(It.IsAny<T>(), It.IsAny<string>(), It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (T item, string id, PartitionKey? pk, ItemRequestOptions o, CancellationToken ct) =>
            {
                await raceOnce();
                return await real.ReplaceItemAsync(item, id, pk, o, ct);
            });
        mock.Setup(c => c.UpsertItemAsync(It.IsAny<T>(), It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (T item, PartitionKey? pk, ItemRequestOptions o, CancellationToken ct) =>
            {
                await raceOnce();
                return await real.UpsertItemAsync(item, pk, o, ct);
            });
    }

    private static DefinitionDraftDocument Draft(string workflowId, string revision) => new()
    {
        Id = $"{workflowId}:draft",
        WorkflowId = workflowId,
        State = "draft",
        BaseVersion = 0,
        DraftRevision = revision,
        Definition = WorkflowDefinitionFixture.MinimalDefinition(),
        LastEditedBy = "user:mark",
        LastEditedUtc = DateTime.UtcNow,
    };

    private static PublishProposalDocument Proposal(string workflowId, string proposalId) => new()
    {
        Id = proposalId,
        WorkflowId = workflowId,
        DraftRevision = "rev-1",
        ProposerId = "user:mark",
        ProposedAtUtc = DateTime.UtcNow,
        ValidationReportRef = new ValidationReportRef { DocumentId = $"{workflowId}:report:rev-1" },
        State = ProposalState.InReview,
    };
}
