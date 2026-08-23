using Frontier.Platform.Hitl;
using Frontier.TestSupport;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.6a tests for <see cref="RequestApprovalActivity"/>.</summary>
public sealed class RequestApprovalActivityTests
{
    [Fact]
    public async Task RunAsync_NullInput_Throws()
    {
        var activity = new RequestApprovalActivity(new FakeApprovalStore());

        await Assert.ThrowsAsync<ArgumentNullException>(() => activity.RunAsync(FakeTaskActivityContext.ForRequestApproval(), null!));
    }

    [Fact]
    public async Task RunAsync_ValidInput_PersistsAndReturnsPendingRequest()
    {
        var store = new FakeApprovalStore();
        var activity = new RequestApprovalActivity(store);
        var input = HitlFixtures.GateOpenRequest();

        var result = await activity.RunAsync(FakeTaskActivityContext.ForRequestApproval(), input);

        Assert.Equal(ApprovalRequestStatus.Pending, result.Status);
        Assert.Equal("eng-1::wf-chain:gate-business-1:0", result.Id);
        Assert.Same(result, store.UpsertedRequest);
    }
}
