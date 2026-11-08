using System.Text.Json;
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

    /// <summary>S13.247: the <c>storage</c> profile may re-run an open that already wrote; the re-run upserts the same request under the same id.</summary>
    [Fact]
    public async Task RunAsync_ReRunWithTheSameInput_UpsertsTheSameRequest()
    {
        var store = new FakeApprovalStore();
        var activity = new RequestApprovalActivity(store);
        var input = HitlFixtures.GateOpenRequest();

        var first = await activity.RunAsync(FakeTaskActivityContext.ForRequestApproval(), input);
        var second = await activity.RunAsync(FakeTaskActivityContext.ForRequestApproval(), input);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Same(second, store.UpsertedRequest);
        Assert.Equal(2, store.Upserts);
    }
}
