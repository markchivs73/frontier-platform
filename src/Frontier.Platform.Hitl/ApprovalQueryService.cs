using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Hitl;

/// <summary>
/// <see cref="IApprovalQueryService"/> implementation over Cosmos (doc 06 §9).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 06 §9); exercised by the Hitl integration tests against the Cosmos emulator (S0.5/CI integration job), not the unit-coverage gate.")]
internal sealed class ApprovalQueryService(CosmosClient client, IOptions<CosmosOptions> options) : IApprovalQueryService
{
    private const string ContainerName = "approvals";

    /// <inheritdoc />
    public async Task<ApprovalRequest?> GetAsync(string approvalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approvalId);

        try
        {
            var container = client.GetContainer(options.Value.Database, ContainerName);
            // Cross-partition point-read: we don't know the engagement_id, so partition key is not available
            var query = container.GetItemQueryIterator<ApprovalRequest>(
                new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
                    .WithParameter("@id", approvalId),
                requestOptions: new QueryRequestOptions { MaxItemCount = 1 });

            var results = await query.ReadNextAsync(cancellationToken);
            return results.FirstOrDefault();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalRequest>> GetByEngagementAsync(string engagementId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engagementId);

        var container = client.GetContainer(options.Value.Database, ContainerName);
        var query = container.GetItemQueryIterator<ApprovalRequest>(
            new QueryDefinition("SELECT * FROM c WHERE c.engagement_id = @engagementId ORDER BY c._ts DESC")
                .WithParameter("@engagementId", engagementId),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(engagementId) });

        var results = new List<ApprovalRequest>();
        while (query.HasMoreResults)
        {
            results.AddRange(await query.ReadNextAsync(cancellationToken));
        }

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Value.Database, ContainerName);
        var query = container.GetItemQueryIterator<ApprovalRequest>(
            new QueryDefinition("SELECT * FROM c WHERE c.status = @status ORDER BY c._ts DESC")
                .WithParameter("@status", ApprovalRequestStatus.Pending.Name),
            requestOptions: new QueryRequestOptions { MaxItemCount = 100 });

        var results = new List<ApprovalRequest>();
        while (query.HasMoreResults)
        {
            results.AddRange(await query.ReadNextAsync(cancellationToken));
        }

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalRequest>> GetEscalatedAsync(CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Value.Database, ContainerName);
        var query = container.GetItemQueryIterator<ApprovalRequest>(
            new QueryDefinition("SELECT * FROM c WHERE c.status = @status ORDER BY c._ts DESC")
                .WithParameter("@status", ApprovalRequestStatus.Escalated.Name),
            requestOptions: new QueryRequestOptions { MaxItemCount = 100 });

        var results = new List<ApprovalRequest>();
        while (query.HasMoreResults)
        {
            results.AddRange(await query.ReadNextAsync(cancellationToken));
        }

        return results.AsReadOnly();
    }
}
