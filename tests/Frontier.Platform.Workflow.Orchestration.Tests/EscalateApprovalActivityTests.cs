using Frontier.Platform.Hitl;
using Frontier.TestSupport;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.6a tests for <see cref="EscalateApprovalActivity"/>.</summary>
public sealed class EscalateApprovalActivityTests
{
    [Fact]
    public async Task RunAsync_NullInput_Throws()
    {
        var activity = new EscalateApprovalActivity(new FakeApprovalStore());

        await Assert.ThrowsAsync<ArgumentNullException>(() => activity.RunAsync(FakeTaskActivityContext.ForEscalateApproval(), null!));
    }

    [Fact]
    public async Task RunAsync_PendingRequest_PersistsAndReturnsEscalatedRequest()
    {
        var store = new FakeApprovalStore();
        var activity = new EscalateApprovalActivity(store);
        var pending = HitlFixtures.PendingRequest();

        var result = await activity.RunAsync(FakeTaskActivityContext.ForEscalateApproval(), pending);

        Assert.Equal(ApprovalRequestStatus.Escalated, result.Status);
        Assert.Equal(pending.Id, result.Id);
        Assert.Same(result, store.UpsertedRequest);
    }
}
