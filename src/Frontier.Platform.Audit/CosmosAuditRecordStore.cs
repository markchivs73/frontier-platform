using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// <see cref="IAuditRecordStore"/> over the <c>audit-records</c> container (doc 02 §3,
/// doc 05 §6): append-only, partitioned by <c>/engagement_id</c>.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 05 §6); exercised by the Audit integration tests against the Cosmos emulator (cosmos-conventions), not the unit-coverage gate.")]
internal sealed class CosmosAuditRecordStore(CosmosClient client, IOptions<CosmosOptions> options) : IAuditRecordStore
{
    /// <summary>The container holding <see cref="SignedAuditRecordDocument"/>s (doc 05 §6).</summary>
    internal const string ContainerName = "audit-records";

    /// <inheritdoc />
    public async Task<SignedAuditRecord?> GetAsync(string executionId, CancellationToken cancellationToken)
    {
        var (engagementId, _) = ExecutionIdParser.Parse(executionId);
        var container = client.GetContainer(options.Value.Database, ContainerName);
        var id = AuditRecordDocumentId.ForExecution(executionId);

        try
        {
            var response = await container.ReadItemAsync<SignedAuditRecordDocument>(id, new PartitionKey(engagementId), cancellationToken: cancellationToken);
            return response.Resource.Record;
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
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.record.closed_at_utc ASC");
        var records = new List<SignedAuditRecord>();

        using var iterator = container.GetItemQueryIterator<SignedAuditRecordDocument>(query, requestOptions: requestOptions);
        while (iterator.HasMoreResults)
        {
            foreach (var document in await iterator.ReadNextAsync(cancellationToken))
            {
                records.Add(document.Record);
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
}
