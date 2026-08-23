using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Lifecycle state of a publish proposal (doc 13 §2–3): a transient review artifact
/// that leaves <see cref="InReview"/> exactly once — decided by a distinct approver
/// (<see cref="Approved"/>/<see cref="Rejected"/>) or auto-withdrawn when its draft is
/// edited (<see cref="Withdrawn"/>). Serializes as a snake_case string (doc 00 §3.5);
/// see <see cref="ProposalDecision"/> for the approver's verb.
/// </summary>
public sealed class ProposalState : SmartEnum<ProposalState>
{
    /// <summary>Awaiting a decision; the only state from which transitions are legal.</summary>
    public static readonly ProposalState InReview = new("in_review");

    /// <summary>Approved — the draft was published as the next immutable version. Terminal.</summary>
    public static readonly ProposalState Approved = new("approved");

    /// <summary>Rejected with a reason; the draft stays editable. Terminal.</summary>
    public static readonly ProposalState Rejected = new("rejected");

    /// <summary>Auto-withdrawn because the draft changed under it (doc 13 §3). Terminal.</summary>
    public static readonly ProposalState Withdrawn = new("withdrawn");

    private ProposalState(string name)
        : base(name)
    {
    }

    /// <summary>Legal transitions per doc 13 §2: only <see cref="InReview"/> may move; decided states are terminal.</summary>
    public bool CanTransitionTo(ProposalState next) => this == InReview && next != InReview;
}
