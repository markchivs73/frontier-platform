using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// <see cref="IMappingProposalStore"/> over <c>model-role-config</c> (ADR-PA32). Proposals share the
/// role's <c>/role_id</c> partition with its mapping versions and its <c>current</c> pointer, which is
/// what allows an approval to be one transactional batch: create <c>{roleId}:v{n}</c>, replace the
/// proposal under its ETag, and repoint <c>current</c> — all or nothing.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 08 §6, ADR-PA32); exercised by the Integration-category emulator tests, not the unit-coverage gate.")]
internal sealed class CosmosMappingProposalStore(CosmosClient client, IOptions<CosmosOptions> options) : IMappingProposalStore
{
    private Container Container => client.GetContainer(options.Value.Database, CosmosRoleRegistry.ContainerName);

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> ListMappingVersionsAsync(string roleId, CancellationToken cancellationToken)
    {
        // Identified positively by the {roleId}:v id prefix rather than by the absence of a doc_type:
        // the pointer is {roleId}:current and a proposal is {roleId}:proposal:{id}, so neither matches,
        // and adding a doc_type to version documents later cannot silently empty this list.
        var query = new QueryDefinition("SELECT VALUE c.mapping_version FROM c WHERE STARTSWITH(c.id, @prefix) AND IS_DEFINED(c.mapping_version) ORDER BY c.mapping_version ASC")
            .WithParameter("@prefix", ModelRoleConfigDocumentId.VersionPrefix(roleId));

        var versions = new List<int>();
        using var iterator = Container.GetItemQueryIterator<int>(query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(roleId) });
        while (iterator.HasMoreResults)
        {
            versions.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return versions;
    }

    /// <inheritdoc />
    public async Task<RoleMapping?> FindMappingVersionAsync(string roleId, int version, CancellationToken cancellationToken)
    {
        try
        {
            var document = await Container.ReadItemAsync<RoleMappingDocument>(
                ModelRoleConfigDocumentId.ForVersion(roleId, version), new PartitionKey(roleId), cancellationToken: cancellationToken);
            return document.Resource.ToDomain();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<int?> FindCurrentVersionAsync(string roleId, CancellationToken cancellationToken)
    {
        try
        {
            var pointer = await Container.ReadItemAsync<RoleMappingCurrentDocument>(
                ModelRoleConfigDocumentId.ForCurrent(roleId), new PartitionKey(roleId), cancellationToken: cancellationToken);
            return pointer.Resource.MappingVersion;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<StoredMappingProposal?> FindProposalAsync(string roleId, string proposalId, CancellationToken cancellationToken)
    {
        try
        {
            var document = await Container.ReadItemAsync<MappingProposalDocument>(
                ModelRoleConfigDocumentId.ForProposal(roleId, proposalId), new PartitionKey(roleId), cancellationToken: cancellationToken);
            return new StoredMappingProposal(document.Resource.ToDomain(), document.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task CreateProposalAsync(MappingChangeProposal proposal, CancellationToken cancellationToken) =>
        await Container.CreateItemAsync(MappingProposalDocument.FromDomain(proposal), new PartitionKey(proposal.RoleId), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryReplaceProposalAsync(MappingChangeProposal proposal, string expectedETag, CancellationToken cancellationToken)
    {
        var document = MappingProposalDocument.FromDomain(proposal);
        try
        {
            await Container.ReplaceItemAsync(document, document.Id, new PartitionKey(proposal.RoleId),
                new ItemRequestOptions { IfMatchEtag = expectedETag }, cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (IsConcurrencyConflict(ex.StatusCode))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryAllocateVersionAsync(
        RoleMapping mapping,
        MappingChangeProposal proposal,
        string expectedETag,
        bool makeCurrent,
        CancellationToken cancellationToken)
    {
        var proposalDocument = MappingProposalDocument.FromDomain(proposal);
        var batch = Container.CreateTransactionalBatch(new PartitionKey(mapping.RoleId))
            .CreateItem(RoleMappingDocument.FromDomain(mapping))
            .ReplaceItem(proposalDocument.Id, proposalDocument, new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedETag });

        if (makeCurrent)
        {
            batch = batch.UpsertItem(RoleMappingCurrentDocument.FromDomain(mapping));
        }

        using var response = await batch.ExecuteAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        return IsConcurrencyConflict(response.StatusCode)
            ? false
            : throw new MappingGovernanceException(
                $"The mapping-version batch for role '{mapping.RoleId}' v{mapping.MappingVersion} failed with status {(int)response.StatusCode}: {response.ErrorMessage}");
    }

    /// <inheritdoc />
    public async Task<StoredMappingProposal?> FindUndecidedProposalAsync(string roleId, CancellationToken cancellationToken)
    {
        var undecided = MappingProposalState.List.Where(state => state.IsAwaitingDecision).Select(state => state.Name).ToArray();
        var query = new QueryDefinition("SELECT * FROM c WHERE c.doc_type = @docType AND ARRAY_CONTAINS(@states, c.state)")
            .WithParameter("@docType", MappingProposalDocument.ProposalDocType)
            .WithParameter("@states", undecided);

        return await FirstOrDefaultAsync(query, roleId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<StoredMappingProposal?> FindProposalByLiveVersionAsync(string roleId, int version, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.doc_type = @docType AND (c.promoted_version = @version OR (NOT IS_DEFINED(c.promoted_version) AND c.mapping_version = @version))")
            .WithParameter("@docType", MappingProposalDocument.ProposalDocType)
            .WithParameter("@version", version);

        return await FirstOrDefaultAsync(query, roleId, cancellationToken);
    }

    /// <summary>The first proposal matching <paramref name="query"/> in the role's partition, with its ETag.</summary>
    private async Task<StoredMappingProposal?> FirstOrDefaultAsync(QueryDefinition query, string roleId, CancellationToken cancellationToken)
    {
        using var iterator = Container.GetItemQueryIterator<MappingProposalDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(roleId), MaxItemCount = 1 });

        while (iterator.HasMoreResults)
        {
            foreach (var document in await iterator.ReadNextAsync(cancellationToken))
            {
                var stored = await FindProposalAsync(roleId, document.ProposalId, cancellationToken);
                if (stored is not null)
                {
                    return stored;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<MappingProposalPage> QueryProposalsAsync(MappingProposalQuery query, CancellationToken cancellationToken)
    {
        using var iterator = Container.GetItemQueryIterator<MappingProposalDocument>(
            MappingProposalQueryBuilder.Build(query),
            query.ContinuationToken,
            new QueryRequestOptions { PartitionKey = new PartitionKey(query.RoleId), MaxItemCount = query.PageSize });

        if (!iterator.HasMoreResults)
        {
            return new MappingProposalPage { Proposals = [] };
        }

        var page = await iterator.ReadNextAsync(cancellationToken);
        return new MappingProposalPage
        {
            Proposals = [.. page.Select(document => document.ToDomain())],
            ContinuationToken = page.ContinuationToken,
        };
    }

    /// <summary>A 412 (the document moved) or 409 (the id is taken) means another writer won; anything else is a real failure.</summary>
    internal static bool IsConcurrencyConflict(HttpStatusCode status) =>
        status is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict;
}

/// <summary>Builds the parameterised proposal query (ADR-PA32). Pure and unit-tested.</summary>
internal static class MappingProposalQueryBuilder
{
    /// <summary>Proposal documents only, optionally filtered by state, most recently proposed first.</summary>
    internal static QueryDefinition Build(MappingProposalQuery query)
    {
        var clauses = "c.doc_type = @docType";
        if (query.State is not null)
        {
            clauses += " AND c.proposal.state = @state";
        }

        var definition = new QueryDefinition($"SELECT * FROM c WHERE {clauses} ORDER BY c.proposal.proposed_at_utc DESC")
            .WithParameter("@docType", MappingProposalDocument.ProposalDocType);

        return query.State is null ? definition : definition.WithParameter("@state", query.State.Name);
    }
}
