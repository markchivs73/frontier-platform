using System.Text.Json;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Hitl.Tests;

/// <summary>Tests for <see cref="ApprovalQueries"/> — what each approvals read selects and how it is scoped (doc 06 §9).</summary>
public sealed class ApprovalQueriesTests
{
    [Fact]
    public void ById_SelectsTheIdAcrossPartitionsAndReadsOneRow()
    {
        var query = ApprovalQueries.ById("ar-1");
        var options = ApprovalQueries.ByIdOptions();

        Assert.Equal("SELECT * FROM c WHERE c.id = @id", query.QueryText);
        Assert.Equal(("@id", (object)"ar-1"), Assert.Single(query.GetQueryParameters()));
        Assert.Equal(1, options.MaxItemCount);
        Assert.Null(options.PartitionKey);
    }

    [Fact]
    public void ByEngagement_StaysInTheEngagementPartitionNewestFirst()
    {
        var query = ApprovalQueries.ByEngagement("eng-1");
        var options = ApprovalQueries.ByEngagementOptions("eng-1");

        Assert.Equal("SELECT * FROM c WHERE c.engagement_id = @engagementId ORDER BY c._ts DESC", query.QueryText);
        Assert.Equal(("@engagementId", (object)"eng-1"), Assert.Single(query.GetQueryParameters()));
        Assert.Equal(new Microsoft.Azure.Cosmos.PartitionKey("eng-1"), options.PartitionKey);
    }

    [Fact]
    public void ByStatus_FiltersOnTheStatusWireNameNewestFirstInPages()
    {
        var query = ApprovalQueries.ByStatus(ApprovalRequestStatus.Escalated);
        var options = ApprovalQueries.ByStatusOptions();

        Assert.Equal("SELECT * FROM c WHERE c.status = @status ORDER BY c._ts DESC", query.QueryText);
        Assert.Equal(("@status", (object)ApprovalRequestStatus.Escalated.Name), Assert.Single(query.GetQueryParameters()));
        Assert.Equal(ApprovalQueries.StatusPageSize, options.MaxItemCount);
        Assert.Null(options.PartitionKey);
    }

    [Fact]
    public void DecidedBy_FiltersOnDecidedStatusDeciderAndWindow_NewestDecisionFirstInPages()
    {
        var query = ApprovalQueries.DecidedBy("oid-1", new DateTime(2026, 11, 24, 9, 30, 0, DateTimeKind.Utc));
        var options = ApprovalQueries.ByStatusOptions();

        Assert.Equal(
            "SELECT * FROM c WHERE c.status = @decided AND c.decision.approver_id = @approverId"
            + " AND c.decision.decided_at_utc >= @since ORDER BY c.decision.decided_at_utc DESC",
            query.QueryText);
        Assert.Equal(
            [("@decided", (object)ApprovalRequestStatus.Decided.Name), ("@approverId", "oid-1"), ("@since", "2026-11-24T09:30:00.000Z")],
            query.GetQueryParameters());
        Assert.Equal(ApprovalQueries.StatusPageSize, options.MaxItemCount);
        Assert.Null(options.PartitionKey);
    }

    [Fact]
    public void DecidedBy_Since_IsWhatTheCanonicalProfileWrites_TruncatedToTheMillisecond()
    {
        var since = new DateTime(2026, 11, 24, 9, 30, 0, DateTimeKind.Utc).AddTicks(12_345_678);

        var parameter = ApprovalQueries.DecidedBy("oid-1", since).GetQueryParameters().Single(p => p.Name == "@since").Value;

        Assert.Equal(JsonSerializer.Serialize(since, CanonicalProfile.Options).Trim('"'), parameter);
        Assert.Equal("2026-11-24T09:30:01.234Z", parameter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DecidedBy_BlankApprover_Throws(string? approverId) =>
        Assert.ThrowsAny<ArgumentException>(() => ApprovalQueries.DecidedBy(approverId!, DateTime.UtcNow));

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void DecidedBy_NonUtcSince_Throws(DateTimeKind kind)
    {
        var ex = Assert.Throws<ArgumentException>(() => ApprovalQueries.DecidedBy("oid-1", new DateTime(2026, 11, 24, 9, 30, 0, kind)));

        Assert.Equal("sinceUtc", ex.ParamName);
    }
}
