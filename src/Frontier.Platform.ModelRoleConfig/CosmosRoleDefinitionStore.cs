using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// <see cref="IRoleDefinitionStore"/> over <c>model-role-config</c> (ADR-PA40). Definition proposals,
/// versions and the pointer share the role's <c>/role_id</c> partition, so an approval is one
/// transactional batch: create <c>{roleId}:definition:v{n}</c>, replace the proposal under its ETag,
/// and upsert <c>{roleId}:definition:current</c> — all or nothing.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK glue (ADR-PA40): SDK calls, feed-draining loops and 404/409/412 translation. Status, batch-outcome, query, first-row and dangling-pointer decisions live in ModelRoleConfigWrites, ModelRoleConfigQueries and ModelRoleConfigReads (S13.193). Exercised by the Integration-category emulator tests.")]
internal sealed class CosmosRoleDefinitionStore(CosmosClient client, IOptions<CosmosOptions> options) : IRoleDefinitionStore
{
    /// <summary>The proposal replace's position in the allocation batch.</summary>
    private const int ProposalReplaceIndex = 1;

    private Container Container => client.GetContainer(options.Value.Database, CosmosRoleRegistry.ContainerName);

    /// <inheritdoc />
    public async Task<RoleDefinition?> FindCurrentDefinitionAsync(string roleId, CancellationToken cancellationToken)
    {
        var pointer = await ReadOrDefaultAsync<RoleDefinitionCurrentDocument>(ModelRoleConfigDocumentId.ForDefinitionCurrent(roleId), roleId, cancellationToken);
        return await ModelRoleConfigReads.FollowDefinitionPointerAsync(
            roleId,
            pointer?.CurrentRef,
            async versionId => (await ReadOrDefaultAsync<RoleDefinitionDocument>(versionId, roleId, cancellationToken))?.ToDomain());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> ListDefinitionVersionsAsync(string roleId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT VALUE c.definition_version FROM c WHERE c.doc_type = @docType")
            .WithParameter("@docType", RoleDefinitionDocument.DefinitionDocType);

        var versions = new List<int>();
        using var iterator = Container.GetItemQueryIterator<int>(query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(roleId) });
        while (iterator.HasMoreResults)
        {
            versions.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        versions.Sort();
        return versions;
    }

    /// <inheritdoc />
    public Task<StoredRoleDefinitionProposal?> FindUndecidedProposalAsync(string roleId, CancellationToken cancellationToken) =>
        ModelRoleConfigReads.FirstResolvedAsync(
            RowsAsync(ModelRoleConfigQueries.UndecidedProposals(RoleDefinitionProposalDocument.ProposalDocType), roleId, cancellationToken),
            document => Task.FromResult<StoredRoleDefinitionProposal?>(
                new StoredRoleDefinitionProposal(document.ToDomain() with { ConcurrencyToken = document.ETag }, document.ETag!)),
            cancellationToken);

    /// <summary>Drains <paramref name="query"/> over the role's partition one row per page.</summary>
    private async IAsyncEnumerable<RoleDefinitionProposalDocument> RowsAsync(
        QueryDefinition query, string roleId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var iterator = Container.GetItemQueryIterator<RoleDefinitionProposalDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(roleId), MaxItemCount = 1 });
        while (iterator.HasMoreResults)
        {
            foreach (var document in await iterator.ReadNextAsync(cancellationToken))
            {
                yield return document;
            }
        }
    }

    /// <inheritdoc />
    public async Task<StoredRoleDefinitionProposal?> FindProposalAsync(string roleId, string proposalId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Container.ReadItemAsync<RoleDefinitionProposalDocument>(
                ModelRoleConfigDocumentId.ForDefinitionProposal(roleId, proposalId), new PartitionKey(roleId), cancellationToken: cancellationToken);
            return new StoredRoleDefinitionProposal(response.Resource.ToDomain() with { ConcurrencyToken = response.ETag }, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task CreateProposalAsync(RoleDefinitionProposal proposal, CancellationToken cancellationToken) =>
        await Container.CreateItemAsync(RoleDefinitionProposalDocument.FromDomain(proposal), new PartitionKey(proposal.RoleId), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<string?> TryReplaceProposalAsync(RoleDefinitionProposal proposal, string expectedETag, CancellationToken cancellationToken)
    {
        var document = RoleDefinitionProposalDocument.FromDomain(proposal);
        try
        {
            var response = await Container.ReplaceItemAsync(document, document.Id, new PartitionKey(proposal.RoleId),
                new ItemRequestOptions { IfMatchEtag = expectedETag }, cancellationToken);
            return response.ETag;
        }
        catch (CosmosException ex) when (ModelRoleConfigWrites.IsConcurrencyConflict(ex.StatusCode))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> TryAllocateDefinitionAsync(
        RoleDefinition definition, RoleDefinitionProposal proposal, string expectedETag, CancellationToken cancellationToken)
    {
        var version = RoleDefinitionDocument.FromDomain(definition);
        var proposalDocument = RoleDefinitionProposalDocument.FromDomain(proposal);
        using var response = await Container.CreateTransactionalBatch(new PartitionKey(definition.RoleId))
            .CreateItem(version)
            .ReplaceItem(proposalDocument.Id, proposalDocument, new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedETag })
            .UpsertItem(RoleDefinitionCurrentDocument.For(version))
            .ExecuteAsync(cancellationToken);

        return ModelRoleConfigWrites.AllocationOutcome(
            response.StatusCode,
            () => response[ProposalReplaceIndex].ETag,
            () => $"The definition-version batch for role '{definition.RoleId}' v{version.DefinitionVersion} failed with status {(int)response.StatusCode}: {response.ErrorMessage}");
    }

    /// <inheritdoc />
    public async Task<RoleDefinitionProposalPage> QueryProposalsAsync(MappingProposalQuery query, CancellationToken cancellationToken)
    {
        using var iterator = Container.GetItemQueryIterator<RoleDefinitionProposalDocument>(
            MappingProposalQueryBuilder.Build(query, RoleDefinitionProposalDocument.ProposalDocType),
            query.ContinuationToken,
            ModelRoleConfigWrites.RequestOptionsFor(query));

        if (!iterator.HasMoreResults)
        {
            return new RoleDefinitionProposalPage { Proposals = [] };
        }

        var page = await iterator.ReadNextAsync(cancellationToken);
        return new RoleDefinitionProposalPage
        {
            Proposals = [.. page.Select(document => document.ToDomain() with { ConcurrencyToken = document.ETag })],
            ContinuationToken = page.ContinuationToken,
        };
    }

    private async Task<T?> ReadOrDefaultAsync<T>(string id, string roleId, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return (await Container.ReadItemAsync<T>(id, new PartitionKey(roleId), cancellationToken: cancellationToken)).Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
