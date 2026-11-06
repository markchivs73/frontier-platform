namespace Frontier.Platform.Hitl.Tests;

/// <summary>S4.6a tests for <see cref="ApprovalRequestStatus"/>.</summary>
public sealed class ApprovalRequestStatusTests
{
    [Fact]
    public void List_Always_ReturnsAllFiveValuesInDeclarationOrder()
    {
        Assert.Equal(
            [ApprovalRequestStatus.Pending, ApprovalRequestStatus.Decided, ApprovalRequestStatus.Escalated, ApprovalRequestStatus.Expired, ApprovalRequestStatus.Withdrawn],
            ApprovalRequestStatus.List);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("decided")]
    [InlineData("escalated")]
    [InlineData("expired")]
    [InlineData("withdrawn")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, ApprovalRequestStatus.FromName(name).Name);
    }

    [Fact]
    public void FromName_UnknownName_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ApprovalRequestStatus.FromName("unknown"));
    }
}
