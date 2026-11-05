using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Hitl;

/// <summary>
/// <see cref="IApprovalQueryService"/> implementation over Cosmos (doc 06 §9). What each read selects
/// is <see cref="ApprovalQueries"/>; this adapter only runs the query and drains its pages.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK glue (doc 06 §9): runs ApprovalQueries' definitions and drains pages, no decisions; exercised by the Hitl integration tests against the Cosmos emulator (CI integration job), not the unit-coverage gate.")]
internal sealed class ApprovalQueryService(CosmosClient client, IOptions<CosmosOptions> options) : IApprovalQueryService
{
    /// <inheritdoc />
    public async Task<ApprovalRequest?> GetAsync(string approvalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approvalId);

        try
        {
            using var query = Container.GetItemQueryIterator<ApprovalRequest>(ApprovalQueries.ById(approvalId), requestOptions: ApprovalQueries.ByIdOptions());
            var results = await query.ReadNextAsync(cancellationToken);
            return results.FirstOrDefault();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalRequest>> GetByEngagementAsync(string engagementId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engagementId);

        return DrainAsync(ApprovalQueries.ByEngagement(engagementId), ApprovalQueries.ByEngagementOptions(engagementId), cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken cancellationToken) =>
        DrainAsync(ApprovalQueries.ByStatus(ApprovalRequestStatus.Pending), ApprovalQueries.ByStatusOptions(), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalRequest>> GetEscalatedAsync(CancellationToken cancellationToken) =>
        DrainAsync(ApprovalQueries.ByStatus(ApprovalRequestStatus.Escalated), ApprovalQueries.ByStatusOptions(), cancellationToken);

    /// <summary>Runs <paramref name="definition"/> and collects every page.</summary>
    private async Task<IReadOnlyList<ApprovalRequest>> DrainAsync(QueryDefinition definition, QueryRequestOptions requestOptions, CancellationToken cancellationToken)
    {
        using var query = Container.GetItemQueryIterator<ApprovalRequest>(definition, requestOptions: requestOptions);
        var results = new List<ApprovalRequest>();
        while (query.HasMoreResults)
        {
            results.AddRange(await query.ReadNextAsync(cancellationToken));
        }

        return results.AsReadOnly();
    }

    /// <summary>The <c>approvals</c> container in the configured database.</summary>
    private Container Container => client.GetContainer(options.Value.Database, CosmosApprovalStore.ContainerName);
}
