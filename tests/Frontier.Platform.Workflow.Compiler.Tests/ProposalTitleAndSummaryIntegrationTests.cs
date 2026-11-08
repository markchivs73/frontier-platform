using System.Net.Http;
using System.Text.Json.Nodes;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;
using Frontier.TestSupport;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.201: the proposer's title and change summary survive the Cosmos store, from the proposal
/// through approval onto the published version, and a proposal without them stores no keys.
/// Runs against the Cosmos emulator on its own database so it never touches another suite's state.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProposalTitleAndSummaryIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string DatabaseId = "frontier-platform-s13201-proposal-text-tests";

    private HttpClientHandler? _handler;
    private HttpClient? _httpClient;
    private CosmosClient? _client;
    private Database? _database;
    private Container? _container;
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
        _container = await _database.CreateContainerIfNotExistsAsync(
            new ContainerProperties { Id = "workflow-definitions", PartitionKeyPath = "/workflowId" });
        _store = new CosmosDefinitionStore(_container);
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DeleteAsync();
        Dispose();
    }

    /// <summary>Disposes the client and the HTTP client and handler this fixture owns.</summary>
    public void Dispose()
    {
        _client?.Dispose();
        _httpClient?.Dispose();
        _handler?.Dispose();
    }

    [Fact]
    public async Task ProposeThenApprove_KeepsTitleAndSummaryOnTheProposalAndTheVersion()
    {
        var lifecycle = Lifecycle();
        var proposalId = await ProposeAsync(lifecycle, "wf-s13201-a", "Email step", "Adds an email delivery node");

        var pending = await _store!.GetProposalAsync(proposalId, CancellationToken.None);
        await lifecycle.ApproveAsync(proposalId, "user:sarah", CancellationToken.None);

        Assert.Equal(("Email step", "Adds an email delivery node"), (pending!.Title, pending.ChangeSummary));
        var approved = await _store.GetProposalAsync(proposalId, CancellationToken.None);
        Assert.Equal(("Email step", "Adds an email delivery node"), (approved!.Title, approved.ChangeSummary));
        var version = await _store.GetVersionAsync("wf-s13201-a", 1, CancellationToken.None);
        Assert.Equal(("Email step", "Adds an email delivery node"), (version!.Title, version.ChangeSummary));
    }

    [Fact]
    public async Task ProposeThenApprove_WithoutText_StoresNoKeys()
    {
        var lifecycle = Lifecycle();
        var proposalId = await ProposeAsync(lifecycle, "wf-s13201-b", title: null, summary: null);

        await lifecycle.ApproveAsync(proposalId, "user:sarah", CancellationToken.None);

        var proposal = (await _container!.ReadItemAsync<JsonObject>(proposalId, new PartitionKey("wf-s13201-b"))).Resource;
        var version = (await _container.ReadItemAsync<JsonObject>("wf-s13201-b:v1", new PartitionKey("wf-s13201-b"))).Resource;
        Assert.False(proposal.ContainsKey("title") || proposal.ContainsKey("changeSummary"));
        Assert.False(version.ContainsKey("title") || version.ContainsKey("changeSummary"));
    }

    private DefinitionLifecycleService Lifecycle()
    {
        var compiler = new Mock<IDefinitionCompiler>().PassesValidation();
        compiler.Setup(c => c.ComputeDefinitionHash(It.IsAny<WorkflowDefinition>())).Returns("sha256:s13201");
        var embedder = new Mock<IDefinitionContractEmbedder>();
        embedder.Setup(e => e.EmbedAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition d, CancellationToken _) => d);
        return new DefinitionLifecycleService(_store!, compiler.Object, embedder.Object, new PublishGovernanceConfig());
    }

    private static async Task<string> ProposeAsync(DefinitionLifecycleService lifecycle, string workflowId, string? title, string? summary)
    {
        var draft = await lifecycle.CreateDraftAsync(workflowId, null, "user:mark", CancellationToken.None);
        var report = new ValidationReport(
            WorkflowId: workflowId,
            DraftRevision: draft.DraftRevision,
            ValidatedAtUtc: DateTime.UtcNow,
            Outcome: ValidationOutcome.Pass,
            Findings: [],
            ResourceVersions: new Dictionary<string, string>());
        var proposal = await lifecycle.ProposePublishAsync(workflowId, draft.DraftRevision, report, "user:mark", title, summary, CancellationToken.None);
        return proposal.ProposalId;
    }
}
