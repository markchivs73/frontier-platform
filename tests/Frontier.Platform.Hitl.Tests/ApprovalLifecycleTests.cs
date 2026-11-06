using Frontier.Platform.Abstractions;
using Frontier.TestSupport;

namespace Frontier.Platform.Hitl.Tests;

/// <summary>S13.149: the guarded withdrawal and escalation over <see cref="ApprovalTransitioner"/>, driven through scripted documents.</summary>
public sealed class ApprovalLifecycleTests
{
    private const string Engagement = "eng-1";
    private const string Actor = "user:manager-1";
    private const string Reason = "Parked run.";

    [Theory]
    [InlineData("pending")]
    [InlineData("escalated")]
    public async Task WithdrawAsync_AwaitingADecision_ReplacesUnderTheReadVersion(string status)
    {
        var request = HitlFixtures.PendingRequest() with { Status = ApprovalRequestStatus.FromName(status) };
        var documents = new ScriptedApprovalDocuments(request);

        var result = await Withdraw(documents, request);

        Assert.Equal(ApprovalTransitionOutcome.Applied, result.Outcome);
        Assert.Equal(ApprovalRequestStatus.Withdrawn, result.Request!.Status);
        Assert.Equal(Actor, result.Request.WithdrawnBy);
        Assert.Equal(Reason, result.Request.WithdrawalReason);
        Assert.Equal(["v1"], documents.ReplaceVersions);
        Assert.Same(result.Request, documents.Stored);
    }

    [Fact]
    public async Task WithdrawAsync_AlreadyDecided_IsRefusedWithoutWriting()
    {
        var decided = ApprovalStatusTransitionsTests.Decided(DecisionKind.Approve);
        var documents = new ScriptedApprovalDocuments(decided);

        var result = await Withdraw(documents, decided);

        Assert.Equal(ApprovalTransitionOutcome.Refused, result.Outcome);
        Assert.Same(decided, result.Request);
        Assert.Empty(documents.ReplaceVersions);
    }

    /// <summary>A decision lands between the withdrawal's read and its replace: the re-read sees it and the withdrawal is refused.</summary>
    [Fact]
    public async Task WithdrawAsync_DecisionWinsTheRace_IsRefusedOnTheReRead()
    {
        var pending = HitlFixtures.PendingRequest();
        var decided = ApprovalStatusTransitionsTests.Decided(DecisionKind.Approve);
        var documents = new ScriptedApprovalDocuments(pending) { LosingReplaces = 1, WinnerOnLoss = decided };

        var result = await Withdraw(documents, pending);

        Assert.Equal(ApprovalTransitionOutcome.Refused, result.Outcome);
        Assert.Same(decided, result.Request);
        Assert.Same(decided, documents.Stored);
        Assert.Equal(["v1"], documents.ReplaceVersions);
    }

    /// <summary>The other side of the race: a withdrawal lands first and the decision is refused, so the gate is never decided after its run was terminated.</summary>
    [Fact]
    public async Task DecideAsync_WithdrawalWinsTheRace_IsRefusedOnTheReRead()
    {
        var pending = HitlFixtures.PendingRequest();
        var withdrawn = ApprovalStatusTransitions.Withdraw(pending, Actor, Reason);
        var documents = new ScriptedApprovalDocuments(pending) { LosingReplaces = 1, WinnerOnLoss = withdrawn };

        var result = await new ApprovalDecider(documents).DecideAsync(Engagement, pending.Id, ApprovalDecisionTransitionTests.DecisionFor(pending), CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.AlreadyDecided, result.Outcome);
        Assert.Same(withdrawn, result.Request);
    }

    [Fact]
    public async Task WithdrawAsync_UnknownRequest_IsNotFound()
    {
        var result = await Withdraw(new ScriptedApprovalDocuments(null), HitlFixtures.PendingRequest());

        Assert.Equal(ApprovalTransitionOutcome.NotFound, result.Outcome);
        Assert.Null(result.Request);
    }

    [Fact]
    public async Task WithdrawAsync_InvalidArguments_Throw()
    {
        var lifecycle = new ApprovalLifecycle(new ScriptedApprovalDocuments(null));

        await Assert.ThrowsAsync<ArgumentException>(() => lifecycle.WithdrawAsync(Engagement, "a", " ", Reason, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => lifecycle.WithdrawAsync(Engagement, "a", Actor, "", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => lifecycle.WithdrawAsync(" ", "a", Actor, Reason, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => lifecycle.WithdrawAsync(Engagement, " ", Actor, Reason, CancellationToken.None));
    }

    [Fact]
    public async Task EscalateAsync_PendingRequest_IsEscalated()
    {
        var pending = HitlFixtures.PendingRequest();
        var documents = new ScriptedApprovalDocuments(pending);

        var result = await new ApprovalLifecycle(documents).EscalateAsync(Engagement, pending.Id, CancellationToken.None);

        Assert.Equal(ApprovalTransitionOutcome.Applied, result.Outcome);
        Assert.Equal(ApprovalRequestStatus.Escalated, documents.Stored!.Status);
    }

    [Fact]
    public async Task EscalateAsync_WithdrawnRequest_IsRefusedWithoutWriting()
    {
        var withdrawn = ApprovalStatusTransitions.Withdraw(HitlFixtures.PendingRequest(), Actor, Reason);
        var documents = new ScriptedApprovalDocuments(withdrawn);

        var result = await new ApprovalLifecycle(documents).EscalateAsync(Engagement, withdrawn.Id, CancellationToken.None);

        Assert.Equal(ApprovalTransitionOutcome.Refused, result.Outcome);
        Assert.Same(withdrawn, documents.Stored);
        Assert.Empty(documents.ReplaceVersions);
    }

    private static Task<ApprovalTransitionResult> Withdraw(ScriptedApprovalDocuments documents, ApprovalRequest request) =>
        new ApprovalLifecycle(documents).WithdrawAsync(Engagement, request.Id, Actor, Reason, CancellationToken.None);
}
