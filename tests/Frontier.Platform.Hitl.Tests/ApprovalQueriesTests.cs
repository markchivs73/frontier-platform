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
}
