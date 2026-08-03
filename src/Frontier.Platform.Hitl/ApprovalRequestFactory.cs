using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Hitl;

/// <summary>
/// Pure construction of a new <see cref="ApprovalRequest"/> from a
/// <see cref="GateOpenRequest"/> (doc 06 §4, §9). Extracted from the request-approval
/// activity shell so the id format, initial status, and escalation-time derivation are
/// independently unit-testable (engineering-standards: no private methods). Public
/// since S11.3 (ADR-PA2): the activity shells live with the consuming solution
/// (Orchestration here), so any solution's own shells build requests through this factory.
/// </summary>
public static class ApprovalRequestFactory
{
    /// <summary>S9.94: prefix of sandbox test-run execution ids (<c>SANDBOX-{guid}::…</c>, S9.38a).</summary>
    private const string SandboxExecutionPrefix = "SANDBOX-";

    /// <summary>S9.94: sandbox approvals self-expire on the same 7-day window as the test-run docs
    /// (S9.38e) so a completed/cleared sandbox run leaves no orphaned pending request behind.</summary>
    private const int SandboxRetentionSeconds = 7 * 24 * 60 * 60;

    /// <summary>
    /// Builds a new <see cref="ApprovalRequestStatus.Pending"/> request. The id is
    /// <c>{executionId}:{gateId}:{occurrence}</c> (doc 06 §9) — distinct per gate visit,
    /// so a re-visit after rollback (doc 06 §13) cannot be satisfied by a stale decision
    /// event. <see cref="ApprovalRequest.EscalateAtUtc"/> is <see langword="null"/> when
    /// <see cref="GateOpenRequest.TimeoutMinutes"/> is <c>0</c> (no escalation, doc 06 §3).
    /// Sandbox gates (S9.94) carry a 7-day TTL; real gates never expire (<c>ttl = -1</c>).
    /// </summary>
    public static ApprovalRequest Open(GateOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ApprovalRequest
        {
            Id = $"{request.ExecutionId}:{request.GateId}:{request.Occurrence}",
            EngagementId = request.EngagementId,
            ExecutionId = request.ExecutionId,
            GateId = request.GateId,
            GateKind = request.GateKind,
            ApproverRoles = request.ApproverRoles,
            SectionRefs = request.SectionRefs,
            Status = ApprovalRequestStatus.Pending,
            RequestedAtUtc = request.RequestedAtUtc,
            EscalateAtUtc = request.TimeoutMinutes > 0 ? request.RequestedAtUtc.AddMinutes(request.TimeoutMinutes) : null,
            Ttl = request.ExecutionId.StartsWith(SandboxExecutionPrefix, StringComparison.Ordinal)
                ? SandboxRetentionSeconds
                : -1,
        };
    }
}
