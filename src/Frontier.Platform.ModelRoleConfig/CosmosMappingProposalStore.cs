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
    public async Task<IReadOnlyList<RoleMapping>> ListMappingsAsync(string roleId, CancellationToken cancellationToken)
    {
        // The same positive {roleId}:v prefix the version listing selects on, reading the documents
        // themselves: a history panel needs every version's ring, chain and reason, and one query
        // beats one point-read per row.
        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix) AND IS_DEFINED(c.mapping_version) ORDER BY c.mapping_version ASC")
            .WithParameter("@prefix", ModelRoleConfigDocumentId.VersionPrefix(roleId));

        var mappings = new List<RoleMapping>();
        using var iterator = Container.GetItemQueryIterator<RoleMappingDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(roleId) });
        while (iterator.HasMoreResults)
        {
            foreach (var document in await iterator.ReadNextAsync(cancellationToken))
            {
                mappings.Add(document.ToDomain());
            }
        }

        return mappings;
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
            return new StoredMappingProposal(document.Resource.ToDomain() with { ConcurrencyToken = document.ETag }, document.ETag);
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
    public async Task<string?> TryReplaceProposalAsync(MappingChangeProposal proposal, string expectedETag, CancellationToken cancellationToken)
    {
        var document = MappingProposalDocument.FromDomain(proposal);
        try
        {
            var response = await Container.ReplaceItemAsync(document, document.Id, new PartitionKey(proposal.RoleId),
                new ItemRequestOptions { IfMatchEtag = expectedETag }, cancellationToken);
            return response.ETag;
        }
        catch (CosmosException ex) when (IsConcurrencyConflict(ex.StatusCode))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> TryAllocateVersionAsync(
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
            // Operation 1 is the proposal replace; its result carries the token the next decision needs.
            return response[ProposalReplaceIndex].ETag;
        }

        return IsConcurrencyConflict(response.StatusCode)
            ? null
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
            RequestOptionsFor(query));

        if (!iterator.HasMoreResults)
        {
            return new MappingProposalPage { Proposals = [] };
        }

        var page = await iterator.ReadNextAsync(cancellationToken);
        return new MappingProposalPage
        {
            Proposals = [.. page.Select(document => document.ToDomain() with { ConcurrencyToken = document.ETag })],
            ContinuationToken = page.ContinuationToken,
        };
    }

    /// <summary>
    /// Single-partition when the query names a role; otherwise a <b>bounded</b> cross-partition fan-out
    /// (ADR-PA34). The page size caps the rows, the continuation token carries the paging, and the
    /// concurrency cap stops one admin screen fanning out across every physical partition at once.
    /// </summary>
    internal static QueryRequestOptions RequestOptionsFor(MappingProposalQuery query) =>
        query.RoleId is { } roleId
            ? new QueryRequestOptions { PartitionKey = new PartitionKey(roleId), MaxItemCount = query.PageSize }
            : new QueryRequestOptions { MaxItemCount = query.PageSize, MaxConcurrency = CrossPartitionConcurrency };

    /// <summary>The fan-out cap for a cross-partition proposal query (ADR-PA34).</summary>
    internal const int CrossPartitionConcurrency = 4;

    /// <summary>The proposal replace's position in the allocation batch.</summary>
    private const int ProposalReplaceIndex = 1;

    /// <summary>A 412 (the document moved) or 409 (the id is taken) means another writer won; anything else is a real failure.</summary>
    internal static bool IsConcurrencyConflict(HttpStatusCode status) =>
        status is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict;
}

/// <summary>Builds the parameterised proposal query (ADR-PA32). Pure and unit-tested.</summary>
internal static class MappingProposalQueryBuilder
{
    /// <summary>
    /// Proposal documents only, optionally filtered by a <b>set</b> of states, most recently proposed
    /// first (ADR-PA34).
    /// <para>
    /// The filtered and ordered paths are <c>c.state</c> and <c>c.proposed_at_utc</c> — the document's
    /// own flat shape. They read <c>c.proposal.*</c> before ADR-PA34, which matched no stored document:
    /// the state filter silently returned nothing and the ordering was arbitrary.
    /// </para>
    /// </summary>
    internal static QueryDefinition Build(MappingProposalQuery query)
    {
        var states = query.States ?? [];
        var clauses = "c.doc_type = @docType";
        if (states.Count > 0)
        {
            clauses += " AND ARRAY_CONTAINS(@states, c.state)";
        }

        var definition = new QueryDefinition($"SELECT * FROM c WHERE {clauses} ORDER BY c.proposed_at_utc DESC")
            .WithParameter("@docType", MappingProposalDocument.ProposalDocType);

        return states.Count == 0
            ? definition
            : definition.WithParameter("@states", states.Select(state => state.Name).ToArray());
    }
}
