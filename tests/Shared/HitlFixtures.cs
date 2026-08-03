using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;

namespace Frontier.TestSupport;

/// <summary>Shared fixtures for S4.6a Hitl tests.</summary>
internal static class HitlFixtures
{
    /// <summary>A <see cref="GateOpenRequest"/> for the PoC <c>gate-business-1</c> (doc 06 §13), first visit.</summary>
    internal static GateOpenRequest GateOpenRequest(int occurrence = 0, int timeoutMinutes = 2) => new()
    {
        ExecutionId = "eng-1::wf-chain",
        EngagementId = "eng-1",
        GateId = "gate-business-1",
        GateKind = GateKind.Business,
        ApproverRoles = ["business-approver"],
        SectionRefs = new Dictionary<string, string>
        {
            ["scope"] = "eng-1::wf-chain:scope:v1",
            ["approach"] = "eng-1::wf-chain:approach:v1",
            ["pricing"] = "eng-1::wf-chain:pricing:v1",
        },
        Occurrence = occurrence,
        TimeoutMinutes = timeoutMinutes,
        RequestedAtUtc = new DateTime(2026, 6, 12, 10, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>A pending <see cref="ApprovalRequest"/> opened from <see cref="GateOpenRequest"/>.</summary>
    internal static ApprovalRequest PendingRequest() => ApprovalRequestFactory.Open(GateOpenRequest());
}
