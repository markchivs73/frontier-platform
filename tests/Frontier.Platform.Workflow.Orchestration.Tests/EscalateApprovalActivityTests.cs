using System.Text.Json;
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
        var pending = HitlFixtures.PendingRequest();
        store.Seed(pending);
        var activity = new EscalateApprovalActivity(store);

        var result = await activity.RunAsync(FakeTaskActivityContext.ForEscalateApproval(), pending);

        Assert.Equal(ApprovalRequestStatus.Escalated, result.Status);
        Assert.Equal(pending.Id, result.Id);
        Assert.Same(result, store.UpsertedRequest);
        Assert.Equal(0, store.Upserts);
    }

    /// <summary>S13.247: the <c>storage</c> profile may re-run an escalation that already applied; the re-run leaves the request escalated and writes nothing.</summary>
    [Fact]
    public async Task RunAsync_ReRunAfterItApplied_ConvergesOnTheSameEscalatedRequest()
    {
        var store = new FakeApprovalStore();
        var pending = HitlFixtures.PendingRequest();
        store.Seed(pending);
        var activity = new EscalateApprovalActivity(store);

        var first = await activity.RunAsync(FakeTaskActivityContext.ForEscalateApproval(), pending);
        var second = await activity.RunAsync(FakeTaskActivityContext.ForEscalateApproval(), pending);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Same(second, store.UpsertedRequest);
        Assert.Equal(ApprovalRequestStatus.Escalated, second.Status);
        Assert.Equal(0, store.Upserts);
    }

    /// <summary>S13.149: an escalation still running when its run is terminated must not re-open the withdrawn request.</summary>
    [Fact]
    public async Task RunAsync_WithdrawnRequest_LeavesItWithdrawnAndReturnsIt()
    {
        var store = new FakeApprovalStore();
        var pending = HitlFixtures.PendingRequest();
        var withdrawn = pending with { Status = ApprovalRequestStatus.Withdrawn, WithdrawnBy = "user:manager-1", WithdrawalReason = "Parked run." };
        store.Seed(withdrawn);

        var result = await new EscalateApprovalActivity(store).RunAsync(FakeTaskActivityContext.ForEscalateApproval(), pending);

        Assert.Same(withdrawn, result);
        Assert.Same(withdrawn, store.UpsertedRequest);
        Assert.Equal(0, store.Upserts);
    }

    /// <summary>The unguarded write rebuilt a lost projection document; the guarded one still does, rather than failing the gate.</summary>
    [Fact]
    public async Task RunAsync_RequestNotStored_WritesTheEscalatedRequestWhole()
    {
        var store = new FakeApprovalStore();
        var pending = HitlFixtures.PendingRequest();

        var result = await new EscalateApprovalActivity(store).RunAsync(FakeTaskActivityContext.ForEscalateApproval(), pending);

        Assert.Equal(ApprovalRequestStatus.Escalated, result.Status);
        Assert.Same(result, store.UpsertedRequest);
        Assert.Equal(1, store.Upserts);
    }
}
