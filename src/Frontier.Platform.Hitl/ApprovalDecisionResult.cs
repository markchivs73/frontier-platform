namespace Frontier.Platform.Hitl;

/// <summary>
/// The result of <see cref="IApprovalStore.DecideAsync"/> (doc 06 §9): the
/// <see cref="Outcome"/> and the request as stored afterwards — the decided request on
/// <see cref="ApprovalDecisionOutcome.Decided"/>, the winning record on
/// <see cref="ApprovalDecisionOutcome.AlreadyDecided"/> (so a 409 can name who decided and
/// when), <see langword="null"/> on <see cref="ApprovalDecisionOutcome.NotFound"/>.
/// </summary>
public sealed record ApprovalDecisionResult
{
    /// <summary>What the store did.</summary>
    public required ApprovalDecisionOutcome Outcome { get; init; }

    /// <summary>The stored request after the call, when one exists.</summary>
    public ApprovalRequest? Request { get; init; }

    /// <summary>The transition applied: <paramref name="request"/> is the decided record now stored.</summary>
    public static ApprovalDecisionResult Decided(ApprovalRequest request) => new() { Outcome = ApprovalDecisionOutcome.Decided, Request = request };

    /// <summary>The transition refused: <paramref name="request"/> is the record as it stands, decision included.</summary>
    public static ApprovalDecisionResult AlreadyDecided(ApprovalRequest request) => new() { Outcome = ApprovalDecisionOutcome.AlreadyDecided, Request = request };

    /// <summary>No such request.</summary>
    public static ApprovalDecisionResult NotFound() => new() { Outcome = ApprovalDecisionOutcome.NotFound };
}
