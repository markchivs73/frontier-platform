using System.Diagnostics.CodeAnalysis;
using System.Net;
using Frontier.Platform.Abstractions;
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

    /// <summary>
    /// How many times a lost ETag race is retried before the caller is told who won. One retry
    /// covers the only legitimate concurrent writer — <c>EscalateApprovalActivity</c>'s upsert —
    /// without looping behind a decision that has already landed.
    /// </summary>
    internal const int DecideAttempts = 2;

    /// <inheritdoc />
    public async Task<ApprovalDecisionResult> DecideAsync(string engagementId, string approvalId, HitlDecision decision, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engagementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        ArgumentNullException.ThrowIfNull(decision);

        var container = client.GetContainer(options.Value.Database, ContainerName);
        var partitionKey = new PartitionKey(engagementId);
        for (var attempt = 0; attempt < DecideAttempts; attempt++)
        {
            var result = await TryDecideOnceAsync(container, partitionKey, approvalId, decision, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        var winner = await ReadAsync(container, approvalId, partitionKey, cancellationToken);
        return winner is null ? ApprovalDecisionResult.NotFound() : ApprovalDecisionResult.AlreadyDecided(winner.Resource);
    }

    /// <summary>
    /// One read-check-replace round: <see langword="null"/> means the replace lost its ETag
    /// precondition (doc 06 §9) and the caller should read again.
    /// </summary>
    internal static async Task<ApprovalDecisionResult?> TryDecideOnceAsync(Container container, PartitionKey partitionKey, string approvalId, HitlDecision decision, CancellationToken cancellationToken)
    {
        var current = await ReadAsync(container, approvalId, partitionKey, cancellationToken);
        if (current is null)
        {
            return ApprovalDecisionResult.NotFound();
        }

        if (!ApprovalDecisionTransition.CanDecide(current.Resource))
        {
            return ApprovalDecisionResult.AlreadyDecided(current.Resource);
        }

        var decided = ApprovalDecisionTransition.Apply(current.Resource, decision);
        try
        {
            await container.ReplaceItemAsync(decided, approvalId, partitionKey, new ItemRequestOptions { IfMatchEtag = current.ETag }, cancellationToken);
            return ApprovalDecisionResult.Decided(decided);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return null;
        }
    }

    /// <summary>Point-reads the request with its ETag; <see langword="null"/> when the id is unknown.</summary>
    internal static async Task<ItemResponse<ApprovalRequest>?> ReadAsync(Container container, string approvalId, PartitionKey partitionKey, CancellationToken cancellationToken)
    {
        try
        {
            return await container.ReadItemAsync<ApprovalRequest>(approvalId, partitionKey, cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
