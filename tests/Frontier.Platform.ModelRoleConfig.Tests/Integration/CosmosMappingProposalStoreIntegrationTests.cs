using Frontier.Platform.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Cosmos;
using static Frontier.Platform.ModelRoleConfig.Tests.MappingProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests.Integration;

/// <summary>
/// S13.101 emulator tests for <see cref="CosmosMappingProposalStore"/> behind
/// <see cref="MappingGovernanceService"/> (ADR-PA32): the create-only version id and the
/// If-Match proposal replace under real concurrency. Provisions its own database, so it does not
/// depend on <c>cosmos-init.py</c>. Follows <c>CosmosGovernanceAuditStoreIntegrationTests</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosMappingProposalStoreIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string DatabaseId = "frontier-mapping-proposal-store-tests";

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
    public async Task ConcurrentApprovalsOnOneRole_CannotBothAllocateTheSameVersion()
    {
        // The property ADR-PA32 rests on: {role}:v{n} is created, never upserted, so of two approvals
        // that both computed n exactly one lands and the other re-reads. The five proposals are seeded
        // through the store rather than ProposeAsync, deliberately bypassing the one-undecided-proposal-
        // per-role governance rule — that rule is about who decides what, and says nothing about whether
        // the allocation underneath it is safe. This is the test that proves the allocation.
        var role = UniqueRole();
        var proposals = await SeedAsync(role, count: 5);
        var service = Service();

        var approved = await Task.WhenAll(proposals.Select(id =>
            service.ApproveAsync(role, id, Approver, "concurrent approval", CancellationToken.None)));

        var versions = approved.Select(proposal => proposal.MappingVersion!.Value).ToList();
        Assert.Equal(versions.Count, versions.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, versions.Count), versions.Order());
    }

    [Fact]
    public async Task ConcurrentApprovalsAcrossRoles_EachAllocateWithinTheirOwnPartition()
    {
        // Versions are per role, so five roles each land v1: the point is that concurrent batches on
        // five partitions do not interfere, and each role's current pointer ends on its own version.
        var roles = Enumerable.Range(0, 5).Select(_ => UniqueRole()).ToList();
        var seeded = new List<(string Role, string ProposalId)>();
        foreach (var role in roles)
        {
            seeded.Add((role, (await SeedAsync(role, count: 1))[0]));
        }

        var service = Service();
        var approved = await Task.WhenAll(seeded.Select(entry =>
            service.ApproveAsync(entry.Role, entry.ProposalId, Approver, "concurrent approval", CancellationToken.None)));

        Assert.All(approved, proposal => Assert.Equal(1, proposal.MappingVersion));
        foreach (var role in roles)
        {
            Assert.Equal(1, await store.FindCurrentVersionAsync(role, CancellationToken.None));
        }
    }

    [Fact]
    public async Task ProposeAsync_SecondProposalForOneRole_IsRefusedUntilTheFirstIsDecided()
    {
        var role = UniqueRole();
        var service = Service();
        var change = ChangeFor(role);

        var first = await service.ProposeAsync(change, Proposer, CancellationToken.None);
        await Assert.ThrowsAsync<Frontier.Platform.Abstractions.ContractViolationException>(() =>
            service.ProposeAsync(change, Proposer, CancellationToken.None));

        await service.RejectAsync(role, first.ProposalId, Approver, "superseded", CancellationToken.None);
        var second = await service.ProposeAsync(change, Proposer, CancellationToken.None);

        Assert.NotEqual(first.ProposalId, second.ProposalId);
    }

    [Fact]
    public async Task ApproveThenPromote_LeavesAFleetVersionCurrentAndTheProposalPromoted()
    {
        var role = UniqueRole();
        var proposalId = (await SeedAsync(role, count: 1))[0];
        var service = Service();

        await service.ApproveAsync(role, proposalId, Approver, "eval accepted", CancellationToken.None);
        var promoted = await service.PromoteAsync(role, proposalId, Approver, "clean week", CancellationToken.None);

        Assert.Equal(MappingProposalState.Promoted, promoted.State);
        Assert.Equal(RolloutRing.Fleet, (await store.FindMappingVersionAsync(role, promoted.PromotedVersion!.Value, CancellationToken.None))!.Ring);
        Assert.Equal(promoted.PromotedVersion, await store.FindCurrentVersionAsync(role, CancellationToken.None));
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

    /// <summary>Stores <paramref name="count"/> pending proposals for <paramref name="role"/> and returns their ids.</summary>
    private async Task<List<string>> SeedAsync(string role, int count)
    {
        var ids = new List<string>();
        for (var index = 0; index < count; index++)
        {
            var proposal = Pending($"p-{index}") with { RoleId = role, PredecessorFleetVersion = null, Change = ChangeFor(role) };
            await store.CreateProposalAsync(proposal, CancellationToken.None);
            ids.Add(proposal.ProposalId);
        }

        return ids;
    }

    private static string UniqueRole() => $"it-role-{Guid.NewGuid():N}";

    /// <summary>Rollback is not exercised here; the pointer is written by the allocation batch.</summary>
    private sealed class NoOpWriter : IRoleMappingWriter
    {
        public Task WriteCurrentAsync(string roleId, int toVersion, CancellationToken ct) => Task.CompletedTask;
    }
}
