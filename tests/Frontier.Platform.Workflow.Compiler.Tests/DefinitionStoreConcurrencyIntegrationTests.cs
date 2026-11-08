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

    /// <summary>S13.248: with no race, one batch approves the proposal, creates the version and moves the pointer.</summary>
    [Fact]
    public async Task ApproveAndPublish_WithNoRace_WritesTheApprovalTheVersionAndThePointerTogether()
    {
        var proposal = await _store!.CreateProposalAsync(Proposal("wf-s13248-a", "prop-s13248-a"), CancellationToken.None);

        Assert.True(await _store.ApproveAndPublishAsync(proposal, Version("wf-s13248-a", 1), CancellationToken.None));

        Assert.Equal(ProposalState.Approved, (await _store.GetProposalAsync("prop-s13248-a", CancellationToken.None))!.State);
        Assert.Equal("published", (await _store.GetVersionAsync("wf-s13248-a", 1, CancellationToken.None))!.State);
        Assert.Equal(1, (await _store.GetCurrentVersionPointerAsync("wf-s13248-a", CancellationToken.None))!.CurrentVersion);
    }

    /// <summary>
    /// S13.248: a failure between the two writes. The version id is already taken, so the batch's
    /// second operation fails after its first, the proposal's approval, would have applied. Cosmos rolls
    /// the approval back: the proposal is still in review, the other version is untouched, no pointer moved.
    /// </summary>
    [Fact]
    public async Task ApproveAndPublish_WhenTheVersionWriteFails_TheApprovalDoesNotLand()
    {
        var proposal = await _store!.CreateProposalAsync(Proposal("wf-s13248-b", "prop-s13248-b"), CancellationToken.None);
        await _store.PublishVersionAsync(Version("wf-s13248-b", 1) with { DefinitionHash = "sha256:other" }, CancellationToken.None);

        Assert.False(await _store.ApproveAndPublishAsync(proposal, Version("wf-s13248-b", 1), CancellationToken.None));

        Assert.Equal(ProposalState.InReview, (await _store.GetProposalAsync("prop-s13248-b", CancellationToken.None))!.State);
        Assert.Equal("sha256:other", (await _store.GetVersionAsync("wf-s13248-b", 1, CancellationToken.None))!.DefinitionHash);
        Assert.Null(await _store.GetCurrentVersionPointerAsync("wf-s13248-b", CancellationToken.None));
    }

    /// <summary>S13.248: a reject that lands between the store's read and its batch wins, and no version is written.</summary>
    [Fact]
    public async Task ApproveAndPublish_WhenARejectLandsFirst_ReturnsFalseAndWritesNoVersion()
    {
        var proposal = await _store!.CreateProposalAsync(Proposal("wf-s13248-c", "prop-s13248-c"), CancellationToken.None);
        var racing = new CosmosDefinitionStore(Interleaving(
            () => _store.RejectProposalAsync("prop-s13248-c", "Not ready", CancellationToken.None)));

        Assert.False(await racing.ApproveAndPublishAsync(proposal, Version("wf-s13248-c", 1), CancellationToken.None));

        Assert.Equal(ProposalState.Rejected, (await _store.GetProposalAsync("prop-s13248-c", CancellationToken.None))!.State);
        Assert.Null(await _store.GetVersionAsync("wf-s13248-c", 1, CancellationToken.None));
        Assert.Null(await _store.GetCurrentVersionPointerAsync("wf-s13248-c", CancellationToken.None));
    }

    [Fact]
    public async Task ApproveAndPublish_RefusesAMissingOrDecidedProposal()
    {
        var decided = await _store!.CreateProposalAsync(Proposal("wf-s13248-d", "prop-s13248-d"), CancellationToken.None);
        Assert.True(await _store.RejectProposalAsync("prop-s13248-d", "No", CancellationToken.None));

        Assert.False(await _store.ApproveAndPublishAsync(decided, Version("wf-s13248-d", 1), CancellationToken.None));
        Assert.False(await _store.ApproveAndPublishAsync(Proposal("wf-s13248-d", "prop-s13248-missing"), Version("wf-s13248-d", 1), CancellationToken.None));
        Assert.Null(await _store.GetVersionAsync("wf-s13248-d", 1, CancellationToken.None));
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
        mock.Setup(c => c.CreateTransactionalBatch(It.IsAny<PartitionKey>()))
            .Returns((PartitionKey pk) =>
            {
                RaceOnceAsync().GetAwaiter().GetResult();
                return real.CreateTransactionalBatch(pk);
            });
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

    private static DefinitionVersionDocument Version(string workflowId, int version) => new()
    {
        Id = $"{workflowId}:v{version}",
        WorkflowId = workflowId,
        State = "published",
        DefinitionVersion = version,
        DefinitionHash = "sha256:s13248",
        Definition = WorkflowDefinitionFixture.MinimalDefinition(),
        ProposedBy = "user:mark",
        ApprovedBy = "user:sarah",
        ProposedUtc = DateTime.UtcNow,
        ApprovedUtc = DateTime.UtcNow,
        ValidationReportRef = $"{workflowId}:report:rev-1",
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
