using Frontier.Platform.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Cosmos;
using static Frontier.Platform.ModelRoleConfig.Tests.MappingProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests.Integration;

/// <summary>
/// S13.101 emulator tests for the ADR-PA34 surface against real Cosmos semantics: the concurrency
/// token is a real ETag, the cross-partition proposals query really does cross partitions and page,
/// the state filter really does match stored documents, and the version history reads the container's
/// own append-only version documents.
/// <para>
/// These are the claims a fake cannot make honestly. The query-path defect ADR-PA34 fixed
/// (<c>c.proposal.state</c> against a flat document) is exactly the class of bug that survives a
/// unit test asserting query <i>text</i>, so the filter is asserted here against stored documents.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosMappingGovernanceSurfaceIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string DatabaseId = "frontier-mapping-governance-surface-tests";

    private CosmosClient client = null!;
    private CosmosMappingProposalStore store = null!;

    public async Task InitializeAsync()
    {
        client = new CosmosClient(Frontier.TestSupport.EmulatorCosmos.Endpoint, Frontier.TestSupport.EmulatorCosmos.Key, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                CheckCertificateRevocationList = true,
            }),
            UseSystemTextJsonSerializerWithOptions = CanonicalProfile.Options,
        });

        var database = await client.CreateDatabaseIfNotExistsAsync(DatabaseId);
        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(CosmosRoleRegistry.ContainerName, "/role_id") { DefaultTimeToLive = -1 });

        store = new CosmosMappingProposalStore(client, Options.Create(new CosmosOptions { Database = DatabaseId }));
    }

    public async Task DisposeAsync() => await client.GetDatabase(DatabaseId).DeleteAsync();

    public void Dispose() => client.Dispose();

    [Fact]
    public async Task AReadProposalCarriesItsRealETag_AndAStaleOneIsRefused()
    {
        var role = UniqueRole();
        var proposalId = await SeedAsync(role);
        var service = Service();

        var read = await service.GetProposalAsync(role, proposalId, CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(read!.ConcurrencyToken));

        // Somebody else decides it, so the token the caller is holding is now stale.
        await service.RejectAsync(role, proposalId, Approver, "superseded", read.ConcurrencyToken, CancellationToken.None);

        var another = await SeedAsync(role);
        var stored = await service.GetProposalAsync(role, another, CancellationToken.None);
        await service.RejectAsync(role, another, Approver, "first decision", stored!.ConcurrencyToken, CancellationToken.None);

        await Assert.ThrowsAsync<MappingConcurrencyConflictException>(() =>
            service.WithdrawAsync(role, another, Proposer, "too late", stored.ConcurrencyToken, CancellationToken.None));
    }

    [Fact]
    public async Task ADecisionReturnsTheTokenItMinted_SoTheNextDecisionNeedsNoReRead()
    {
        var role = UniqueRole();
        var proposalId = await SeedAsync(role);
        var service = Service();

        var read = await service.GetProposalAsync(role, proposalId, CancellationToken.None);
        var approved = await service.ApproveAsync(role, proposalId, Approver, "eval accepted", read!.ConcurrencyToken, CancellationToken.None);

        // The token approval handed back is the one the batch's proposal replace produced.
        var promoted = await service.PromoteAsync(role, proposalId, Approver, "clean week", approved.ConcurrencyToken, CancellationToken.None);

        Assert.Equal(MappingProposalState.Promoted, promoted.State);
    }

    [Fact]
    public async Task TheCrossPartitionQueryReturnsProposalsFromEveryRole()
    {
        var roles = new[] { UniqueRole(), UniqueRole(), UniqueRole() };
        foreach (var role in roles)
        {
            await SeedAsync(role);
        }

        var page = await Service().ListProposalsAsync(
            new MappingProposalQuery { States = [MappingProposalState.PendingApproval], PageSize = 200 }, CancellationToken.None);

        Assert.All(roles, role => Assert.Contains(page.Proposals, proposal => proposal.RoleId == role));
    }

    [Fact]
    public async Task TheStateFilterMatchesStoredDocuments()
    {
        // The ADR-PA34 defect: the filter read c.proposal.state, which no stored document has, so it
        // silently matched nothing. Asserted against real documents rather than against query text.
        var role = UniqueRole();
        var decided = await SeedAsync(role);
        var service = Service();
        await service.RejectAsync(role, decided, Approver, "no evidence", null, CancellationToken.None);
        var pendingId = await SeedAsync(role);

        var pending = await service.ListProposalsAsync(
            new MappingProposalQuery { RoleId = role, States = [MappingProposalState.PendingApproval] }, CancellationToken.None);
        var rejected = await service.ListProposalsAsync(
            new MappingProposalQuery { RoleId = role, States = [MappingProposalState.Rejected] }, CancellationToken.None);
        var everything = await service.ListProposalsAsync(new MappingProposalQuery { RoleId = role }, CancellationToken.None);

        Assert.Equal(pendingId, Assert.Single(pending.Proposals).ProposalId);
        Assert.Equal(decided, Assert.Single(rejected.Proposals).ProposalId);
        Assert.Equal(2, everything.Proposals.Count);
    }

    [Fact]
    public async Task TheCrossPartitionQueryPagesWithItsContinuationToken()
    {
        var roles = new[] { UniqueRole(), UniqueRole(), UniqueRole() };
        foreach (var role in roles)
        {
            await SeedAsync(role);
        }

        var service = Service();
        var seen = new List<string>();
        string? token = null;
        do
        {
            var page = await service.ListProposalsAsync(
                new MappingProposalQuery { States = [MappingProposalState.PendingApproval], PageSize = 1, ContinuationToken = token },
                CancellationToken.None);
            seen.AddRange(page.Proposals.Select(proposal => proposal.ProposalId));
            token = page.ContinuationToken;
        }
        while (token is not null);

        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.True(seen.Count >= roles.Length);
    }

    [Fact]
    public async Task TheVersionHistoryReadsEveryStoredVersionAndMarksTheCurrentOne()
    {
        var role = UniqueRole();
        var proposalId = await SeedAsync(role);
        var service = Service();
        var approved = await service.ApproveAsync(role, proposalId, Approver, "eval accepted", null, CancellationToken.None);
        await service.PromoteAsync(role, proposalId, Approver, "clean week", approved.ConcurrencyToken, CancellationToken.None);

        var history = await new MappingVersionHistory(store).GetVersionHistoryAsync(role, CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Equal(RolloutRing.Canary, history[0].Ring);
        Assert.Equal(RolloutRing.Fleet, history[1].Ring);
        Assert.False(history[0].IsCurrent);
        Assert.True(history[1].IsCurrent);
        Assert.Equal(Approver, history[1].ApprovedBy);
    }

    /// <summary>The governance service over the real store.</summary>
    private MappingGovernanceService Service() =>
        new(store, new NoOpWriter(), new FakeMappingDecisionRecorder(), TimeProvider.System,
            Options.Create(new MappingGovernanceOptions { DecisionMaxAttempts = 50, DecisionBaseDelayMs = 5, DecisionMaxDelayMs = 200 }));

    /// <summary>A well-formed change targeting <paramref name="role"/>.</summary>
    private static MappingChange ChangeFor(string role)
    {
        var change = Change();
        return change with { RoleId = role, ProposedMapping = change.ProposedMapping with { RoleId = role } };
    }

    /// <summary>Stores one pending proposal for <paramref name="role"/> and returns its id.</summary>
    private async Task<string> SeedAsync(string role)
    {
        var proposal = Pending($"p-{Guid.NewGuid():N}") with { RoleId = role, PredecessorFleetVersion = null, Change = ChangeFor(role) };
        await store.CreateProposalAsync(proposal, CancellationToken.None);
        return proposal.ProposalId;
    }

    private static string UniqueRole() => $"it-surface-{Guid.NewGuid():N}";

    /// <summary>The allocation batch writes the pointer; rollback is not exercised here.</summary>
    private sealed class NoOpWriter : IRoleMappingWriter
    {
        public Task WriteCurrentAsync(string roleId, int toVersion, CancellationToken ct) => Task.CompletedTask;
    }
}
