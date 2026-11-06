using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Hitl;

/// <summary>
/// What a guarded status transition of <see cref="IApprovalStore"/> did (S13.149):
/// <see cref="IApprovalStore.WithdrawAsync"/> and <see cref="IApprovalStore.EscalateAsync"/>. Modelled on
/// <see cref="ApprovalDecisionOutcome"/>: the transition applied, or was refused because the stored request
/// no longer allows it, or the request does not exist. Serializes as a snake_case string (doc 00 §3.5).
/// </summary>
public sealed class ApprovalTransitionOutcome : SmartEnum<ApprovalTransitionOutcome>
{
    /// <summary>The transition is now stored.</summary>
    public static readonly ApprovalTransitionOutcome Applied = new("applied");

    /// <summary>The stored request no longer allows the transition (it was decided, expired or withdrawn); it is returned unchanged.</summary>
    public static readonly ApprovalTransitionOutcome Refused = new("refused");

    /// <summary>No request with that id exists in that engagement.</summary>
    public static readonly ApprovalTransitionOutcome NotFound = new("not_found");

    private ApprovalTransitionOutcome(string name)
        : base(name)
    {
    }
}
