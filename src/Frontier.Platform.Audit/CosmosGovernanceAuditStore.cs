using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// <see cref="IGovernanceAuditStore"/> over <c>governance-audit-records</c> (ADR-PA30): partition key
/// <c>/scope</c>, container TTL -1. An append is one transactional batch on the scope's partition:
/// create the record, create its record-id marker, and create or If-Match-replace the head.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (ADR-PA30); exercised by the Integration-category emulator tests, not the unit-coverage gate.")]
internal sealed class CosmosGovernanceAuditStore(CosmosClient client, IOptions<CosmosOptions> options) : IGovernanceAuditStore
{
    /// <summary>The container.</summary>
    internal const string ContainerName = "governance-audit-records";

    /// <summary>The container's partition key path.</summary>
    internal const string PartitionKeyPath = "/scope";

    private Container Container => client.GetContainer(options.Value.Database, ContainerName);

    /// <inheritdoc />
    public async Task<GovernanceAuditHeadState?> ReadHeadAsync(string scope, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Container.ReadItemAsync<GovernanceAuditHeadDocument>(GovernanceAuditDocumentId.ForHead(scope), new PartitionKey(scope), cancellationToken: cancellationToken);
            return new GovernanceAuditHeadState(response.Resource.ToHead(), response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<SignedGovernanceAuditRecord?> FindInScopeAsync(string scope, string recordId, CancellationToken cancellationToken)
    {
        try
        {
            var partitionKey = new PartitionKey(scope);
            var marker = await Container.ReadItemAsync<GovernanceAuditRecordIdDocument>(GovernanceAuditDocumentId.ForRecordId(recordId), partitionKey, cancellationToken: cancellationToken);
            var document = await Container.ReadItemAsync<GovernanceAuditRecordDocument>(GovernanceAuditDocumentId.ForRecord(scope, marker.Resource.Sequence), partitionKey, cancellationToken: cancellationToken);
            return document.Resource.Record;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryAppendAsync(SignedGovernanceAuditRecord record, string? expectedHeadETag, CancellationToken cancellationToken)
    {
        var head = GovernanceAuditHeadDocument.FromRecord(record);
        var batch = Container.CreateTransactionalBatch(new PartitionKey(record.Scope))
            .CreateItem(GovernanceAuditRecordDocument.FromRecord(record))
            .CreateItem(GovernanceAuditRecordIdDocument.FromRecord(record));

        batch = expectedHeadETag is null
            ? batch.CreateItem(head)
            : batch.ReplaceItem(head.Id, head, new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedHeadETag });

        using var response = await batch.ExecuteAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        return GovernanceAuditConflict.IsConcurrencyConflict(response.StatusCode)
            ? false
            : throw new GovernanceAuditAppendException(string.Create(CultureInfo.InvariantCulture,
                $"The governance audit batch for scope '{record.Scope}' failed with status {(int)response.StatusCode}: {response.ErrorMessage}"));
    }

    /// <inheritdoc />
    public async Task<SignedGovernanceAuditRecord?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.doc_type = @docType AND c.record.record_id = @recordId")
            .WithParameter("@docType", GovernanceAuditDocumentId.RecordDocType)
            .WithParameter("@recordId", recordId);

        var records = await ReadAllAsync(query, new QueryRequestOptions(), cancellationToken);
        return records.Count > 0 ? records[0] : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SignedGovernanceAuditRecord>> GetChainAsync(string scope, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.doc_type = @docType ORDER BY c.id ASC")
            .WithParameter("@docType", GovernanceAuditDocumentId.RecordDocType);

        return await ReadAllAsync(query, new QueryRequestOptions { PartitionKey = new PartitionKey(scope) }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GovernanceAuditPage> QueryAsync(GovernanceAuditQuery query, CancellationToken cancellationToken)
    {
        var requestOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(query.Scope), MaxItemCount = query.PageSize };
        using var iterator = Container.GetItemQueryIterator<GovernanceAuditRecordDocument>(GovernanceAuditQueryBuilder.Build(query), query.ContinuationToken, requestOptions);

        if (!iterator.HasMoreResults)
        {
            return new GovernanceAuditPage { Records = [] };
        }

        var page = await iterator.ReadNextAsync(cancellationToken);
        return new GovernanceAuditPage { Records = [.. page.Select(document => document.Record)], ContinuationToken = page.ContinuationToken };
    }

    private async Task<List<SignedGovernanceAuditRecord>> ReadAllAsync(QueryDefinition query, QueryRequestOptions requestOptions, CancellationToken cancellationToken)
    {
        var records = new List<SignedGovernanceAuditRecord>();
        using var iterator = Container.GetItemQueryIterator<GovernanceAuditRecordDocument>(query, requestOptions: requestOptions);

        while (iterator.HasMoreResults)
        {
            records.AddRange((await iterator.ReadNextAsync(cancellationToken)).Select(document => document.Record));
        }

        return records;
    }
}
