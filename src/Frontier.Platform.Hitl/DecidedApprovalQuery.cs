using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Hitl;

/// <summary>
/// The doc 12 §8 recovery-sweep's gate-reraise query: every <see cref="ApprovalRequest"/>
/// whose <see cref="ApprovalRequest.Status"/> is <see cref="ApprovalRequestStatus.Decided"/>.
/// Cross-partition by design (the recovery worker scans all engagements), kept separate
/// from <see cref="CosmosApprovalStore"/>'s single-document writes (cosmos-conventions:
/// "a new cross-partition query in a hot path is a design smell" — this is a cold,
/// periodic sweep, not a hot path).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (doc 12 §8); exercised by the Hitl integration tests against the Cosmos emulator (S0.5/CI integration job), not the unit-coverage gate.")]
internal sealed class DecidedApprovalQuery(CosmosClient client, IOptions<CosmosOptions> options)
{
    /// <summary>The doc 12 §8 recovery-sweep predicate, parameterised on <see cref="ApprovalRequestStatus.Decided"/>.</summary>
    private static readonly QueryDefinition Query = new QueryDefinition(
            "SELECT * FROM c WHERE c.status = @decided")
        .WithParameter("@decided", ApprovalRequestStatus.Decided.Name);

    /// <summary>Executes the recovery-sweep query, returning every decided approval request.</summary>
    internal async Task<IReadOnlyList<ApprovalRequest>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Value.Database, CosmosApprovalStore.ContainerName);
        var results = new List<ApprovalRequest>();

        using var iterator = container.GetItemQueryIterator<ApprovalRequest>(Query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }
}
