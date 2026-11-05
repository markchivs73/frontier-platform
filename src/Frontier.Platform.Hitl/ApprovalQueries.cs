using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Hitl;

/// <summary>
/// The parameterised queries and request options behind <see cref="ApprovalQueryService"/> (doc 06 §9):
/// which documents each read selects, in which order, and whether it stays in one partition. Kept apart
/// from the coverage-excluded adapter (S13.193) so what each query asks for is unit-tested.
/// </summary>
internal static class ApprovalQueries
{
    /// <summary>The page size of the fleet-wide status queries.</summary>
    internal const int StatusPageSize = 100;

    /// <summary>One request by id, across partitions: the caller does not know its engagement.</summary>
    internal static QueryDefinition ById(string approvalId) =>
        new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", approvalId);

    /// <summary>Request options for <see cref="ById"/>: at most one row.</summary>
    internal static QueryRequestOptions ByIdOptions() => new() { MaxItemCount = 1 };

    /// <summary>Every request of one engagement, newest first.</summary>
    internal static QueryDefinition ByEngagement(string engagementId) =>
        new QueryDefinition("SELECT * FROM c WHERE c.engagement_id = @engagementId ORDER BY c._ts DESC")
            .WithParameter("@engagementId", engagementId);

    /// <summary>Request options for <see cref="ByEngagement"/>: scoped to the engagement's partition.</summary>
    internal static QueryRequestOptions ByEngagementOptions(string engagementId) =>
        new() { PartitionKey = new PartitionKey(engagementId) };

    /// <summary>Every request in <paramref name="status"/>, newest first, across engagements.</summary>
    internal static QueryDefinition ByStatus(ApprovalRequestStatus status) =>
        new QueryDefinition("SELECT * FROM c WHERE c.status = @status ORDER BY c._ts DESC")
            .WithParameter("@status", status.Name);

    /// <summary>Request options for <see cref="ByStatus"/>: paged at <see cref="StatusPageSize"/>.</summary>
    internal static QueryRequestOptions ByStatusOptions() => new() { MaxItemCount = StatusPageSize };
}
