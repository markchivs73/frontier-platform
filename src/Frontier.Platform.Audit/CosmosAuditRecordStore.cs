using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// <see cref="IAuditRecordStore"/> over the <c>audit-records</c> container (doc 02 §3,
/// doc 05 §6): append-only, partitioned by <c>/engagement_id</c>. It also implements
/// <see cref="IAuditChainHeadStore"/> (ADR-PA31): the head document shares the container and the
/// engagement's partition, which is what lets one transactional batch write the record and move the
/// head together.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 05 §6, ADR-PA31); exercised by the Audit integration tests against the Cosmos emulator (cosmos-conventions), not the unit-coverage gate.")]
internal sealed class CosmosAuditRecordStore(CosmosClient client, IOptions<CosmosOptions> options) : IAuditRecordStore, IAuditChainHeadStore
{
    /// <summary>The container holding <see cref="SignedAuditRecordDocument"/>s (doc 05 §6).</summary>
    internal const string ContainerName = "audit-records";

    /// <inheritdoc />
    public async Task<SignedAuditRecord?> GetAsync(string executionId, string engagementId, CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Value.Database, ContainerName);
        var id = AuditRecordDocumentId.ForExecution(executionId);

        try
        {
            var response = await container.ReadItemAsync<SignedAuditRecordDocument>(id, new PartitionKey(engagementId), cancellationToken: cancellationToken);
            return AuditRecordSchemaGuard.EnsureReadable(response.Resource.Record);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SignedAuditRecord>> GetChainAsync(string engagementId, CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Value.Database, ContainerName);
        var requestOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(engagementId) };
        var query = new QueryDefinition($"SELECT * FROM c WHERE {AuditRecordDocumentId.RecordDocumentPredicate} ORDER BY c.record.closed_at_utc ASC");
        var records = new List<SignedAuditRecord>();

        using var iterator = container.GetItemQueryIterator<SignedAuditRecordDocument>(query, requestOptions: requestOptions);
        while (iterator.HasMoreResults)
        {
            foreach (var document in await iterator.ReadNextAsync(cancellationToken))
            {
                records.Add(AuditRecordSchemaGuard.EnsureReadable(document.Record));
            }
        }

        return records;
    }

    /// <inheritdoc />
    public Task CreateAsync(SignedAuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var container = client.GetContainer(options.Value.Database, ContainerName);
        var document = SignedAuditRecordDocument.FromRecord(record);
        return container.CreateItemAsync(document, new PartitionKey(document.EngagementId), cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AuditChainHeadState?> ReadHeadAsync(string engagementId, CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Value.Database, ContainerName);

        try
        {
            var response = await container.ReadItemAsync<AuditChainHeadDocument>(
                AuditRecordDocumentId.ForHead(engagementId), new PartitionKey(engagementId), cancellationToken: cancellationToken);
            return new AuditChainHeadState(response.Resource.ToHead(), response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryAppendAsync(SignedAuditRecord record, AuditChainHead head, string? expectedHeadETag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(head);

        var container = client.GetContainer(options.Value.Database, ContainerName);
        var headDocument = AuditChainHeadDocument.FromHead(head);
        var batch = container.CreateTransactionalBatch(new PartitionKey(record.EngagementId))
            .CreateItem(SignedAuditRecordDocument.FromRecord(record));

        batch = expectedHeadETag is null
            ? batch.CreateItem(headDocument)
            : batch.ReplaceItem(headDocument.Id, headDocument, new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedHeadETag });

        using var response = await batch.ExecuteAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        return AuditChainConflict.IsConcurrencyConflict(response.StatusCode)
            ? false
            : throw new AuditChainAppendException(string.Create(CultureInfo.InvariantCulture,
                $"The audit append batch for engagement '{record.EngagementId}' failed with status {(int)response.StatusCode}: {response.ErrorMessage}"));
    }
}
