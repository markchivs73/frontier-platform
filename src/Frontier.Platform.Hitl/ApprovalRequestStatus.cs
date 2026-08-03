using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Hitl;

/// <summary>
/// The lifecycle of an <see cref="ApprovalRequest"/> (doc 06 §9). Serializes as a
/// snake_case string, identical to a standard enum (doc 00 §3.5).
/// </summary>
public sealed class ApprovalRequestStatus : SmartEnum<ApprovalRequestStatus>
{
    /// <summary>Awaiting a decision; the gate's <c>WaitForExternalEvent</c> is still open.</summary>
    public static readonly ApprovalRequestStatus Pending = new("pending");

    /// <summary>A decision has been recorded (doc 06 §9, embedded in <see cref="ApprovalRequest.Decision"/>).</summary>
    public static readonly ApprovalRequestStatus Decided = new("decided");

    /// <summary>The gate's escalation timer fired before a decision arrived (doc 06 §7); still awaiting a decision.</summary>
    public static readonly ApprovalRequestStatus Escalated = new("escalated");

    /// <summary>Reporting-only terminal state for an unattended gate (doc 06 §7); the platform never auto-decides.</summary>
    public static readonly ApprovalRequestStatus Expired = new("expired");

    private ApprovalRequestStatus(string name)
        : base(name)
    {
    }
}
