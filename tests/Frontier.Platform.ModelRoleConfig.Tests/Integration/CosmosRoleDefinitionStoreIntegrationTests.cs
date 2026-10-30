using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using static Frontier.Platform.ModelRoleConfig.Tests.RoleDefinitionProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests.Integration;

/// <summary>
/// ADR-PA40 emulator tests for <see cref="CosmosRoleDefinitionStore"/> driven through the real
/// <see cref="RoleDefinitionGovernanceService"/>: the approval batch creates the version, moves the
/// proposal and the pointer together, the catalogue then serves the new role, and the mapping
/// documents and queries in the same partition are untouched. Provisions its own database.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosRoleDefinitionStoreIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string DatabaseId = "frontier-role-definition-governance-tests";

    private CosmosClient client = null!;
    private CosmosRoleDefinitionStore store = null!;
    private CosmosRoleRegistry registry = null!;
    private CosmosMappingProposalStore mappings = null!;

    public async Task InitializeAsync()
    {
        client = new CosmosClient(Frontier.TestSupport.EmulatorCosmos.Endpoint, Frontier.TestSupport.EmulatorCosmos.Key, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            UseSystemTextJsonSerializerWithOptions = CanonicalProfile.Options,
        });

        var database = await client.CreateDatabaseIfNotExistsAsync(DatabaseId);
        await database.Database.CreateContainerIfNotExistsAsync(new ContainerProperties(CosmosRoleRegistry.ContainerName, "/role_id") { DefaultTimeToLive = -1 });

        var options = Options.Create(new CosmosOptions { Database = DatabaseId });
        store = new CosmosRoleDefinitionStore(client, options);
        registry = new CosmosRoleRegistry(client, options);
        mappings = new CosmosMappingProposalStore(client, options);
    }

    public async Task DisposeAsync() => await client.GetDatabase(DatabaseId).DeleteAsync();

    public void Dispose() => client.Dispose();

    [Fact]
    public async Task ProposeThenApprove_MintsV1_MovesThePointer_AndTheCatalogueServesTheRole()
    {
        var service = Service(store, new FakeMappingDecisionRecorder());

        var proposal = await service.ProposeAsync(AgentDefinition(), Proposer, Reason, CancellationToken.None);
        var approved = await service.ApproveAsync(AgentRoleId, proposal.ProposalId, Approver, "reviewed", proposal.ConcurrencyToken, CancellationToken.None);

        Assert.Equal(1, approved.DefinitionVersion);
        Assert.Equal(approved.ConcurrencyToken, (await store.FindProposalAsync(AgentRoleId, proposal.ProposalId, CancellationToken.None))!.ETag);
        var current = await store.FindCurrentDefinitionAsync(AgentRoleId, CancellationToken.None);
        Assert.Equal(approved.ProposedDefinition, current);
        Assert.Equal(approved.ProposedDefinition, Assert.Single((await registry.GetCatalogueAsync(CancellationToken.None)).Roles));
        Assert.Equal([1], await store.ListDefinitionVersionsAsync(AgentRoleId, CancellationToken.None));
    }

    [Fact]
    public async Task ASecondApproval_AllocatesV2_AndOnlyOneProposalIsUndecidedAtATime()
    {
        var service = Service(store, new FakeMappingDecisionRecorder());
        var first = await service.ProposeAsync(AgentDefinition(), Proposer, Reason, CancellationToken.None);
        Assert.Equal(first.ProposalId, (await store.FindUndecidedProposalAsync(AgentRoleId, CancellationToken.None))!.Proposal.ProposalId);
        await service.ApproveAsync(AgentRoleId, first.ProposalId, Approver, "v1", null, CancellationToken.None);
        Assert.Null(await store.FindUndecidedProposalAsync(AgentRoleId, CancellationToken.None));

        var second = await service.ProposeAsync(AgentDefinition() with { Description = "v2 wording" }, Proposer, Reason, CancellationToken.None);
        var approved = await service.ApproveAsync(AgentRoleId, second.ProposalId, Approver, "v2", null, CancellationToken.None);

        Assert.Equal((1, 2), (second.PredecessorDefinitionVersion, approved.DefinitionVersion));
        Assert.Equal([1, 2], await store.ListDefinitionVersionsAsync(AgentRoleId, CancellationToken.None));
        Assert.Equal("v2 wording", (await store.FindCurrentDefinitionAsync(AgentRoleId, CancellationToken.None))!.Description);
    }

    [Fact]
    public async Task ReplaceWithAStaleETag_LosesAndChangesNothing()
    {
        var proposal = Pending();
        await store.CreateProposalAsync(proposal, CancellationToken.None);
        var stored = await store.FindProposalAsync(AgentRoleId, proposal.ProposalId, CancellationToken.None);

        var token = await store.TryReplaceProposalAsync(proposal with { State = MappingProposalState.Withdrawn }, stored!.ETag, CancellationToken.None);
        var lost = await store.TryReplaceProposalAsync(proposal with { State = MappingProposalState.Rejected }, stored.ETag, CancellationToken.None);

        Assert.NotNull(token);
        Assert.Null(lost);
        Assert.Equal(MappingProposalState.Withdrawn, (await store.FindProposalAsync(AgentRoleId, proposal.ProposalId, CancellationToken.None))!.Proposal.State);
    }

    [Fact]
    public async Task AllocatingATakenVersion_IsAConflict_AndTheProposalStaysPending()
    {
        var proposal = Pending();
        await store.CreateProposalAsync(proposal, CancellationToken.None);
        var stored = await store.FindProposalAsync(AgentRoleId, proposal.ProposalId, CancellationToken.None);
        var approved = RoleDefinitionProposalDocumentTests.Approved with { DefinitionVersion = 1, ProposedDefinition = RoleDefinitionProposalDocumentTests.Approved.ProposedDefinition with { DefinitionVersion = 1 } };
        Assert.NotNull(await store.TryAllocateDefinitionAsync(approved.ProposedDefinition, approved, stored!.ETag, CancellationToken.None));

        var again = await store.TryAllocateDefinitionAsync(approved.ProposedDefinition, approved, (await store.FindProposalAsync(AgentRoleId, proposal.ProposalId, CancellationToken.None))!.ETag, CancellationToken.None);

        Assert.Null(again);
    }

    [Fact]
    public async Task QueryProposals_FiltersByStateAcrossRoles_AndNeverReturnsMappingProposals()
    {
        await store.CreateProposalAsync(Pending(), CancellationToken.None);
        await store.CreateProposalAsync(Pending(ModelDefinition(), "d-2") with { State = MappingProposalState.Rejected }, CancellationToken.None);
        await mappings.CreateProposalAsync(MappingProposalSamples.Pending(), CancellationToken.None);

        var pending = await store.QueryProposalsAsync(new MappingProposalQuery { States = [MappingProposalState.PendingApproval] }, CancellationToken.None);
        var all = await store.QueryProposalsAsync(new MappingProposalQuery(), CancellationToken.None);

        Assert.Equal("d-1", Assert.Single(pending.Proposals).ProposalId);
        Assert.Equal(["d-1", "d-2"], all.Proposals.Select(proposal => proposal.ProposalId).Order());
        Assert.All(all.Proposals, proposal => Assert.NotNull(proposal.ConcurrencyToken));
    }

    [Fact]
    public async Task DefinitionDocuments_LeaveTheRolesMappingHistoryAndUndecidedMappingSlotUnchanged()
    {
        var service = Service(store, new FakeMappingDecisionRecorder());
        var proposal = await service.ProposeAsync(ModelDefinition() with { Description = "reworded" }, Proposer, Reason, CancellationToken.None);
        await service.ApproveAsync("deep-reasoning", proposal.ProposalId, Approver, "ok", null, CancellationToken.None);
        await service.ProposeAsync(ModelDefinition(), Proposer, Reason, CancellationToken.None);

        Assert.Empty(await mappings.ListMappingVersionsAsync("deep-reasoning", CancellationToken.None));
        Assert.Empty(await mappings.ListMappingsAsync("deep-reasoning", CancellationToken.None));
        Assert.Null(await mappings.FindUndecidedProposalAsync("deep-reasoning", CancellationToken.None));
        Assert.Empty((await mappings.QueryProposalsAsync(new MappingProposalQuery(), CancellationToken.None)).Proposals);
    }

    [Fact]
    public async Task FindCurrentDefinition_BeforeAnyApproval_IsNull() =>
        Assert.Null(await store.FindCurrentDefinitionAsync(AgentRoleId, CancellationToken.None));
}
