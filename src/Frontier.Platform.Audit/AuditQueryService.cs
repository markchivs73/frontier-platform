using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// <see cref="IAuditQueryService"/> backing the <c>/api/audit/*</c> endpoints (doc 05 §7,
/// §10, S5.7). <see cref="GetAsync"/> and <see cref="GetChainAsync"/> delegate to
/// <see cref="IAuditRecordStore"/>; <see cref="QueryAsync"/> runs <see cref="AuditQueryBuilder"/>'s
/// projection directly against <c>audit-records</c> (cosmos-conventions: governance queries
/// may be cross-partition).
/// </summary>
internal sealed class AuditQueryService(IAuditRecordStore recordStore, CosmosClient client, IOptions<CosmosOptions> options) : IAuditQueryService
{
    /// <inheritdoc />
    public Task<SignedAuditRecord?> GetAsync(string executionId, CancellationToken cancellationToken) =>
        recordStore.GetAsync(executionId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SignedAuditRecord>> GetChainAsync(string engagementId, CancellationToken cancellationToken) =>
        recordStore.GetChainAsync(engagementId, cancellationToken);

    /// <inheritdoc />
    [ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 05 §7); exercised by the Audit integration tests against the Cosmos emulator (cosmos-conventions), not the unit-coverage gate.")]
    public async Task<IReadOnlyList<AuditSummary>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Value.Database, CosmosAuditRecordStore.ContainerName);
        var definition = AuditQueryBuilder.Build(query);
        var summaries = new List<AuditSummary>();

        using var iterator = container.GetItemQueryIterator<AuditSummary>(definition);
        while (iterator.HasMoreResults)
        {
            summaries.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return summaries;
    }
}
