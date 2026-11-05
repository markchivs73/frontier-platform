using System.Diagnostics.CodeAnalysis;
using System.Net;
using Frontier.Platform.Abstractions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Hitl;

/// <summary>
/// <see cref="IApprovalStore"/> over the <c>approvals</c> container (doc 02 §3, doc 06 §9).
/// </summary>
/// <remarks>
/// SDK glue only: the decision loop and its lost-race retry live in <see cref="ApprovalDecider"/>,
/// which runs over this adapter's <see cref="IApprovalDocuments"/> half (S13.193).
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK glue (doc 02 §3, doc 06 §9): status-code translation only, its decisions live in ApprovalDecider; exercised by the Hitl integration tests against the Cosmos emulator (CI integration job), not the unit-coverage gate.")]
internal sealed class CosmosApprovalStore(CosmosClient client, IOptions<CosmosOptions> options, DecidedApprovalQuery decidedQuery) : IApprovalStore, IApprovalDocuments
{
    /// <summary>The container holding <see cref="ApprovalRequest"/> documents (doc 02 §3).</summary>
    internal const string ContainerName = "approvals";

    /// <inheritdoc />
    public Task UpsertAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Container.UpsertItemAsync(request, new PartitionKey(request.EngagementId), cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalRequest>> GetDecidedAsync(CancellationToken cancellationToken) =>
        decidedQuery.ExecuteAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ApprovalDecisionResult> DecideAsync(string engagementId, string approvalId, HitlDecision decision, CancellationToken cancellationToken) =>
        new ApprovalDecider(this).DecideAsync(engagementId, approvalId, decision, cancellationToken);

    /// <inheritdoc />
    public async Task<VersionedApproval?> ReadAsync(string engagementId, string approvalId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Container.ReadItemAsync<ApprovalRequest>(approvalId, new PartitionKey(engagementId), cancellationToken: cancellationToken);
            return new VersionedApproval(response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryReplaceAsync(ApprovalRequest request, string expectedVersion, CancellationToken cancellationToken)
    {
        try
        {
            await Container.ReplaceItemAsync(request, request.Id, new PartitionKey(request.EngagementId), new ItemRequestOptions { IfMatchEtag = expectedVersion }, cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
    }

    /// <summary>The <c>approvals</c> container in the configured database.</summary>
    private Container Container => client.GetContainer(options.Value.Database, ContainerName);
}
