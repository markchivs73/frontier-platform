using System.Net;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The decisions the <c>model-role-config</c> stores make around their SDK calls (ADR-PA32, ADR-PA34, ADR-PA40):
/// which statuses mean another writer won, what an allocation batch's result means, and how a proposal query is
/// scoped. Kept apart from the coverage-excluded <see cref="CosmosMappingProposalStore"/> and
/// <see cref="CosmosRoleDefinitionStore"/> (S13.193) so the unit gate measures them.
/// </summary>
internal static class ModelRoleConfigWrites
{
    /// <summary>The fan-out cap for a cross-partition proposal query (ADR-PA34).</summary>
    internal const int CrossPartitionConcurrency = 4;

    /// <summary>A 412 (the document moved) or 409 (the id is taken) means another writer won; anything else is a real failure.</summary>
    internal static bool IsConcurrencyConflict(HttpStatusCode status) =>
        status is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict;

    /// <summary>
    /// What an allocation batch's status means for the caller: the proposal's new ETag when the batch landed,
    /// <see langword="null"/> when another writer won, and a <see cref="MappingGovernanceException"/> naming
    /// <paramref name="describeFailure"/> for any other failure.
    /// </summary>
    internal static string? AllocationOutcome(HttpStatusCode status, Func<string?> proposalETag, Func<string> describeFailure)
    {
        if ((int)status is >= 200 and <= 299)
        {
            return proposalETag();
        }

        return IsConcurrencyConflict(status) ? null : throw new MappingGovernanceException(describeFailure());
    }

    /// <summary>
    /// Single-partition when the query names a role; otherwise a <b>bounded</b> cross-partition fan-out
    /// (ADR-PA34). The page size caps the rows, the continuation token carries the paging, and the
    /// concurrency cap stops one admin screen fanning out across every physical partition at once.
    /// </summary>
    internal static QueryRequestOptions RequestOptionsFor(MappingProposalQuery query) =>
        query.RoleId is { } roleId
            ? new QueryRequestOptions { PartitionKey = new PartitionKey(roleId), MaxItemCount = query.PageSize }
            : new QueryRequestOptions { MaxItemCount = query.PageSize, MaxConcurrency = CrossPartitionConcurrency };
}
