using Frontier.Platform.Abstractions;
using Frontier.TestSupport;

namespace Frontier.Platform.Hitl.Tests;

/// <summary>Tests for <see cref="ApprovalDecisionTransition"/> — the pure half of doc 06 §9's <c>pending → decided</c> write.</summary>
public sealed class ApprovalDecisionTransitionTests
{
    [Fact]
    public void CanDecide_PendingRequest_IsTrue() =>
        Assert.True(ApprovalDecisionTransition.CanDecide(HitlFixtures.PendingRequest()));

    [Fact]
    public void CanDecide_EscalatedRequest_IsTrue() =>
        Assert.True(ApprovalDecisionTransition.CanDecide(HitlFixtures.PendingRequest() with { Status = ApprovalRequestStatus.Escalated }));

    [Fact]
    public void CanDecide_DecidedRequest_IsFalse() =>
        Assert.False(ApprovalDecisionTransition.CanDecide(Decided(HitlFixtures.PendingRequest())));

    [Fact]
    public void CanDecide_ExpiredRequest_IsFalse() =>
        Assert.False(ApprovalDecisionTransition.CanDecide(HitlFixtures.PendingRequest() with { Status = ApprovalRequestStatus.Expired }));

    [Fact]
    public void CanDecide_NullRequest_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ApprovalDecisionTransition.CanDecide(null!));

    [Fact]
    public void Apply_PendingRequest_EmbedsDecisionAndKeepsEverythingElse()
    {
        var request = HitlFixtures.PendingRequest();
        var decision = DecisionFor(request);

        var decided = ApprovalDecisionTransition.Apply(request, decision);

        Assert.Equal(ApprovalRequestStatus.Decided, decided.Status);
        Assert.Same(decision, decided.Decision);
        Assert.Equal(request with { Status = ApprovalRequestStatus.Decided, Decision = decision }, decided);
        Assert.Equal(ApprovalRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void Apply_DecisionForAnotherRequest_Throws()
    {
        var request = HitlFixtures.PendingRequest();
        var stale = DecisionFor(request) with { RequestId = $"{request.Id[..^1]}9" };

        var ex = Assert.Throws<ArgumentException>(() => ApprovalDecisionTransition.Apply(request, stale));

        Assert.Equal("decision", ex.ParamName);
        Assert.Contains(request.Id, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_NullArguments_Throw()
    {
        var request = HitlFixtures.PendingRequest();

        Assert.Throws<ArgumentNullException>(() => ApprovalDecisionTransition.Apply(null!, DecisionFor(request)));
        Assert.Throws<ArgumentNullException>(() => ApprovalDecisionTransition.Apply(request, null!));
    }

    [Fact]
    public void Results_CarryTheOutcomeAndTheRecord()
    {
        var request = HitlFixtures.PendingRequest();

        Assert.Equal(ApprovalDecisionOutcome.Decided, ApprovalDecisionResult.Decided(request).Outcome);
        Assert.Same(request, ApprovalDecisionResult.Decided(request).Request);
        Assert.Equal(ApprovalDecisionOutcome.AlreadyDecided, ApprovalDecisionResult.AlreadyDecided(request).Outcome);
        Assert.Same(request, ApprovalDecisionResult.AlreadyDecided(request).Request);
        Assert.Equal(ApprovalDecisionOutcome.NotFound, ApprovalDecisionResult.NotFound().Outcome);
        Assert.Null(ApprovalDecisionResult.NotFound().Request);
    }

    [Fact]
    public void Outcomes_SerializeAsSnakeCaseNames()
    {
        Assert.Equal("decided", ApprovalDecisionOutcome.Decided.Name);
        Assert.Equal("already_decided", ApprovalDecisionOutcome.AlreadyDecided.Name);
        Assert.Equal("not_found", ApprovalDecisionOutcome.NotFound.Name);
    }

    internal static HitlDecision DecisionFor(ApprovalRequest request) => new()
    {
        GateId = request.GateId,
        RequestId = request.Id,
        ApproverId = "user:approver-1",
        Kind = DecisionKind.Approve,
        Notes = "ok",
        DecidedAtUtc = new DateTime(2026, 6, 12, 11, 0, 0, DateTimeKind.Utc),
    };

    internal static ApprovalRequest Decided(ApprovalRequest request) =>
        ApprovalDecisionTransition.Apply(request, DecisionFor(request));
}
