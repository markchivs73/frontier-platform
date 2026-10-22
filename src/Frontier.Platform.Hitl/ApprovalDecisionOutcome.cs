using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Hitl;

/// <summary>
/// What <see cref="IApprovalStore.DecideAsync"/> did with a decision (doc 06 §9): the single
/// <c>pending → decided</c> transition applied, or was refused because the request had already
/// been decided (or is otherwise no longer awaiting one), or the request does not exist.
/// Serializes as a snake_case string, identical to a standard enum (doc 00 §3.5).
/// </summary>
public sealed class ApprovalDecisionOutcome : SmartEnum<ApprovalDecisionOutcome>
{
    /// <summary>The decision is now embedded in the stored request; the caller raises the gate event next.</summary>
    public static readonly ApprovalDecisionOutcome Decided = new("decided");

    /// <summary>Another decision won, or the request is no longer awaiting one; the stored record is returned unchanged.</summary>
    public static readonly ApprovalDecisionOutcome AlreadyDecided = new("already_decided");

    /// <summary>No request with that id exists in that engagement.</summary>
    public static readonly ApprovalDecisionOutcome NotFound = new("not_found");

    private ApprovalDecisionOutcome(string name)
        : base(name)
    {
    }
}
