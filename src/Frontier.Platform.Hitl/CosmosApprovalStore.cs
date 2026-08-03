using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Hitl;

/// <summary>
/// <see cref="IApprovalStore"/> over the <c>approvals</c> container (doc 02 §3, doc 06 §9).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 02 §3, doc 06 §9); exercised by the Hitl integration tests against the Cosmos emulator (S0.5/CI integration job), not the unit-coverage gate.")]
internal sealed class CosmosApprovalStore(CosmosClient client, IOptions<CosmosOptions> options, DecidedApprovalQuery decidedQuery) : IApprovalStore
{
    /// <summary>The container holding <see cref="ApprovalRequest"/> documents (doc 02 §3).</summary>
    internal const string ContainerName = "approvals";

    /// <inheritdoc />
    public Task UpsertAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var container = client.GetContainer(options.Value.Database, ContainerName);
        return container.UpsertItemAsync(request, new PartitionKey(request.EngagementId), cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalRequest>> GetDecidedAsync(CancellationToken cancellationToken) =>
        decidedQuery.ExecuteAsync(cancellationToken);
}
