using System.Text.Json;
using Frontier.Platform.Serialization;
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

    /// <summary>Request options for <see cref="ByStatus"/> and <see cref="DecidedBy"/>: across engagements, paged at <see cref="StatusPageSize"/>.</summary>
    internal static QueryRequestOptions ByStatusOptions() => new() { MaxItemCount = StatusPageSize };

    /// <summary>
    /// The requests <paramref name="approverId"/> decided at or after <paramref name="sinceUtc"/> and still
    /// <c>decided</c>, newest decision first (S13.312's bounded read for C1's "Decided by you"). Cross-partition
    /// by nature, because one approver's decisions span engagements; it is accepted because the decider and the
    /// window bound it through the index, it is intended for the consumer's C1 "Decided by you" read (its only
    /// planned caller), and the single-partition
    /// alternative (a per-approver projection) is deferred (docs/DECISIONS.md ADR-PA49). Equal
    /// <c>decided_at_utc</c> values have no defined order across partitions: the query names no second sort key,
    /// so a caller that needs a stable order breaks ties itself.
    /// </summary>
    /// <remarks>
    /// <c>@since</c> is the canonical wire string (doc 01 §3.2, fixed-width UTC to the millisecond), so the
    /// comparison is a string comparison over the stored bytes and orders as the dates do. The canonical
    /// converter truncates to the millisecond, so the result is a superset at the sub-millisecond edge.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="approverId"/> is blank, or <paramref name="sinceUtc"/> is not UTC.</exception>
    internal static QueryDefinition DecidedBy(string approverId, DateTime sinceUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approverId);
        if (sinceUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The window's start must be a UTC time.", nameof(sinceUtc));
        }

        return new QueryDefinition(
                "SELECT * FROM c WHERE c.status = @decided AND c.decision.approver_id = @approverId"
                + " AND c.decision.decided_at_utc >= @since ORDER BY c.decision.decided_at_utc DESC")
            .WithParameter("@decided", ApprovalRequestStatus.Decided.Name)
            .WithParameter("@approverId", approverId)
            .WithParameter("@since", WireTime(sinceUtc));
    }

    /// <summary><paramref name="value"/> exactly as the canonical profile writes it, without the JSON quotes.</summary>
    private static string WireTime(DateTime value) => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(value, CanonicalProfile.Options))!;
}
