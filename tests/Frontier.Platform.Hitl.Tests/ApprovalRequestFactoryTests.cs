using Frontier.Platform.Abstractions;
using Frontier.TestSupport;

namespace Frontier.Platform.Hitl.Tests;

/// <summary>S4.6a tests for <see cref="ApprovalRequestFactory"/>.</summary>
public sealed class ApprovalRequestFactoryTests
{
    [Fact]
    public void Open_FirstVisit_BuildsDeterministicIdAndPendingStatus()
    {
        var request = ApprovalRequestFactory.Open(HitlFixtures.GateOpenRequest(occurrence: 0));

        Assert.Equal("eng-1::wf-chain:gate-business-1:0", request.Id);
        Assert.Equal("eng-1", request.EngagementId);
        Assert.Equal("eng-1::wf-chain", request.ExecutionId);
        Assert.Equal("gate-business-1", request.GateId);
        Assert.Equal(GateKind.Business, request.GateKind);
        Assert.Equal(["business-approver"], request.ApproverRoles);
        Assert.Equal(3, request.SectionRefs.Count);
        Assert.Equal(ApprovalRequestStatus.Pending, request.Status);
        Assert.Null(request.Decision);
    }

    [Fact]
    public void Open_SecondVisit_IncludesOccurrenceInId()
    {
        var request = ApprovalRequestFactory.Open(HitlFixtures.GateOpenRequest(occurrence: 1));

        Assert.Equal("eng-1::wf-chain:gate-business-1:1", request.Id);
    }

    [Fact]
    public void Open_PositiveTimeout_DerivesEscalateAtUtcFromRequestedAtUtc()
    {
        var openRequest = HitlFixtures.GateOpenRequest(timeoutMinutes: 2);

        var request = ApprovalRequestFactory.Open(openRequest);

        Assert.Equal(openRequest.RequestedAtUtc.AddMinutes(2), request.EscalateAtUtc);
    }

    [Fact]
    public void Open_ZeroTimeout_EscalateAtUtcIsNull()
    {
        var request = ApprovalRequestFactory.Open(HitlFixtures.GateOpenRequest(timeoutMinutes: 0));

        Assert.Null(request.EscalateAtUtc);
    }

    [Fact]
    public void Open_RealExecution_NeverExpires()
    {
        var request = ApprovalRequestFactory.Open(HitlFixtures.GateOpenRequest());

        Assert.Equal(-1, request.Ttl); // -1 = no TTL; real approvals persist as evidence
    }

    [Fact]
    public void Open_SandboxExecution_CarriesSevenDayTtl()
    {
        // S9.94: a sandbox test-run gate must self-expire so a completed/cleared run leaves no
        // orphaned pending request (and it never appears in the human inbox — see ApprovalsController).
        var sandbox = HitlFixtures.GateOpenRequest() with { ExecutionId = "SANDBOX-abc123::wf-chain" };

        var request = ApprovalRequestFactory.Open(sandbox);

        Assert.Equal(7 * 24 * 60 * 60, request.Ttl);
    }
}
