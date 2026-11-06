namespace Frontier.Platform.Hitl;

/// <summary>
/// The result of <see cref="IApprovalStore.WithdrawAsync"/> or <see cref="IApprovalStore.EscalateAsync"/>
/// (S13.149): the <see cref="Outcome"/> and the request as stored afterwards — the transitioned request on
/// <see cref="ApprovalTransitionOutcome.Applied"/>, the record that refused it on
/// <see cref="ApprovalTransitionOutcome.Refused"/> (so a caller can say who decided or withdrew it), and
/// <see langword="null"/> on <see cref="ApprovalTransitionOutcome.NotFound"/>.
/// </summary>
public sealed record ApprovalTransitionResult
{
    /// <summary>What the store did.</summary>
    public required ApprovalTransitionOutcome Outcome { get; init; }

    /// <summary>The stored request after the call, when one exists.</summary>
    public ApprovalRequest? Request { get; init; }

    /// <summary>The transition applied: <paramref name="request"/> is the record now stored.</summary>
    public static ApprovalTransitionResult Applied(ApprovalRequest request) => new() { Outcome = ApprovalTransitionOutcome.Applied, Request = request };

    /// <summary>The transition refused: <paramref name="request"/> is the record as it stands.</summary>
    public static ApprovalTransitionResult Refused(ApprovalRequest request) => new() { Outcome = ApprovalTransitionOutcome.Refused, Request = request };

    /// <summary>No such request.</summary>
    public static ApprovalTransitionResult NotFound() => new() { Outcome = ApprovalTransitionOutcome.NotFound };
}
