using Frontier.Platform.Abstractions;
using Frontier.TestSupport;

namespace Frontier.Platform.Hitl.Tests;

/// <summary>
/// S13.149: which statuses each guarded transition accepts. <see cref="Classification"/> places every
/// status explicitly, and <see cref="Classification_CoversExactlyEveryStatus"/> fails when a value of
/// <see cref="ApprovalRequestStatus.List"/> is missing from it, so a status added later cannot slip
/// through unclassified.
/// </summary>
public sealed class ApprovalStatusTransitionsTests
{
    /// <summary>
    /// Every status, and whether a request in it can be decided, withdrawn or escalated, and whether it is
    /// final. A decided request here holds an approve; the escalate-decision case is tested separately.
    /// </summary>
    private static readonly Dictionary<string, (bool Decidable, bool Withdrawable, bool Escalatable, bool Final)> Classification = new(StringComparer.Ordinal)
    {
        ["pending"] = (true, true, true, false),
        ["escalated"] = (true, true, true, false),
        ["decided"] = (false, false, false, true),
        ["expired"] = (false, false, false, true),
        ["withdrawn"] = (false, false, false, true),
    };

    [Fact]
    public void Classification_CoversExactlyEveryStatus() =>
        Assert.Equal(Classification.Keys.Order(StringComparer.Ordinal), ApprovalRequestStatus.List.Select(status => status.Name).Order(StringComparer.Ordinal));

    [Fact]
    public void EveryStatus_BehavesAsItsClassificationSays()
    {
        foreach (var status in ApprovalRequestStatus.List)
        {
            var expected = Classification[status.Name];
            var request = InStatus(status);

            Assert.Equal(expected, (ApprovalDecisionTransition.CanDecide(request), ApprovalStatusTransitions.CanWithdraw(request), ApprovalStatusTransitions.CanEscalate(request), !ApprovalDecisionTransition.CanDecide(request)));
        }
    }

    [Fact]
    public void CanDecide_AcrossEveryStatus_IsExactlyPendingAndEscalated() =>
        Assert.Equal([ApprovalRequestStatus.Pending, ApprovalRequestStatus.Escalated], Accepted(ApprovalDecisionTransition.CanDecide));

    [Fact]
    public void CanWithdraw_AcrossEveryStatus_IsExactlyPendingAndEscalated() =>
        Assert.Equal([ApprovalRequestStatus.Pending, ApprovalRequestStatus.Escalated], Accepted(ApprovalStatusTransitions.CanWithdraw));

    /// <summary>A decided request is escalated only when its decision is the approver's own escalate; the walk uses an approve.</summary>
    [Fact]
    public void CanEscalate_AcrossEveryStatus_IsExactlyPendingAndEscalated() =>
        Assert.Equal([ApprovalRequestStatus.Pending, ApprovalRequestStatus.Escalated], Accepted(ApprovalStatusTransitions.CanEscalate));

    [Theory]
    [InlineData("escalate", true)]
    [InlineData("approve", false)]
    [InlineData("reject", false)]
    public void CanEscalate_DecidedRequest_OnlyWhenTheDecisionIsAnEscalate(string kind, bool expected) =>
        Assert.Equal(expected, ApprovalStatusTransitions.CanEscalate(Decided(DecisionKind.FromName(kind))));

    /// <summary>A decided status with no embedded decision is malformed; it is never treated as an escalate.</summary>
    [Fact]
    public void CanEscalate_DecidedWithoutADecision_IsFalse() =>
        Assert.False(ApprovalStatusTransitions.CanEscalate(HitlFixtures.PendingRequest() with { Status = ApprovalRequestStatus.Decided }));

    [Fact]
    public void Withdraw_RecordsWhoAndWhyAndKeepsEverythingElse()
    {
        var request = HitlFixtures.PendingRequest();

        var withdrawn = ApprovalStatusTransitions.Withdraw(request, "user:manager-1", "Parked run.");

        Assert.Equal(request with { Status = ApprovalRequestStatus.Withdrawn, WithdrawnBy = "user:manager-1", WithdrawalReason = "Parked run." }, withdrawn);
    }

    [Fact]
    public void Withdraw_InvalidArguments_Throw()
    {
        var request = HitlFixtures.PendingRequest();

        Assert.Throws<ArgumentNullException>(() => ApprovalStatusTransitions.Withdraw(null!, "user:a", "why"));
        Assert.Throws<ArgumentException>(() => ApprovalStatusTransitions.Withdraw(request, " ", "why"));
        Assert.Throws<ArgumentException>(() => ApprovalStatusTransitions.Withdraw(request, "user:a", ""));
    }

    [Fact]
    public void Escalate_ReopensTheRequestWithoutTheEscalateDecision()
    {
        var decided = Decided(DecisionKind.Escalate);

        var escalated = ApprovalStatusTransitions.Escalate(decided);

        Assert.Equal(decided with { Status = ApprovalRequestStatus.Escalated, Decision = null }, escalated);
    }

    [Fact]
    public void NullRequest_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ApprovalStatusTransitions.CanEscalate(null!));
        Assert.Throws<ArgumentNullException>(() => ApprovalStatusTransitions.Escalate(null!));
        Assert.Throws<ArgumentNullException>(() => ApprovalStatusTransitions.CanWithdraw(null!));
    }

    [Fact]
    public void Results_CarryTheOutcomeAndTheRecord()
    {
        var request = HitlFixtures.PendingRequest();

        Assert.Equal(ApprovalTransitionOutcome.Applied, ApprovalTransitionResult.Applied(request).Outcome);
        Assert.Same(request, ApprovalTransitionResult.Applied(request).Request);
        Assert.Equal(ApprovalTransitionOutcome.Refused, ApprovalTransitionResult.Refused(request).Outcome);
        Assert.Same(request, ApprovalTransitionResult.Refused(request).Request);
        Assert.Equal(ApprovalTransitionOutcome.NotFound, ApprovalTransitionResult.NotFound().Outcome);
        Assert.Null(ApprovalTransitionResult.NotFound().Request);
    }

    [Fact]
    public void Outcomes_SerializeAsSnakeCaseNames() =>
        Assert.Equal(["applied", "refused", "not_found"], ApprovalTransitionOutcome.List.Select(outcome => outcome.Name));

    internal static ApprovalRequest Decided(DecisionKind kind)
    {
        var request = HitlFixtures.PendingRequest();
        return ApprovalDecisionTransition.Apply(request, ApprovalDecisionTransitionTests.DecisionFor(request) with { Kind = kind });
    }

    private static List<ApprovalRequestStatus> Accepted(Func<ApprovalRequest, bool> accepts) =>
        [.. ApprovalRequestStatus.List.Where(status => accepts(InStatus(status)))];

    /// <summary>A request in <paramref name="status"/>; a decided one holds an approve.</summary>
    private static ApprovalRequest InStatus(ApprovalRequestStatus status) =>
        status == ApprovalRequestStatus.Decided ? Decided(DecisionKind.Approve) : HitlFixtures.PendingRequest() with { Status = status };
}
