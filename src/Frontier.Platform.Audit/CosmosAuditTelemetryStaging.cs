using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// <see cref="IAuditTelemetryStaging"/> over the <c>audit-telemetry-staging</c> container
/// (doc 05 §9, C-14).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 05 §9); exercised by the Audit integration tests against the Cosmos emulator (cosmos-conventions), not the unit-coverage gate.")]
internal sealed class CosmosAuditTelemetryStaging(CosmosClient client, IOptions<CosmosOptions> options) : IAuditTelemetryStaging
{
    /// <summary>The container holding <see cref="AuditTelemetryStagingDocument"/>s (doc 05 §9).</summary>
    internal const string ContainerName = "audit-telemetry-staging";

    /// <inheritdoc />
    public Task RecordInvocationAsync(AuditTelemetryRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var container = client.GetContainer(options.Value.Database, ContainerName);
        var document = AuditTelemetryStagingDocument.FromRecord(record);
        return container.UpsertItemAsync(document, new PartitionKey(document.ExecutionId), cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditTelemetryRecord>> GetForExecutionAsync(string executionId, CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Value.Database, ContainerName);
        var requestOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(executionId) };
        var records = new List<AuditTelemetryRecord>();

        using var iterator = container.GetItemQueryIterator<AuditTelemetryStagingDocument>(new QueryDefinition("SELECT * FROM c ORDER BY c.record.invoked_at_utc ASC"), requestOptions: requestOptions);
        while (iterator.HasMoreResults)
        {
            foreach (var document in await iterator.ReadNextAsync(cancellationToken))
            {
                records.Add(document.Record);
            }
        }

        return records;
    }
}
