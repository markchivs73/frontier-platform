using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Where a <see cref="MappingChangeProposal"/> sits in the doc 08 §7 governance loop (ADR-PA32).
/// Serializes as a snake_case string via <c>SmartEnumJsonConverterFactory</c> (doc 01 ADR-C1).
/// <para>
/// <see cref="Shadow"/> is declared but never entered by this release: shadow execution is
/// sequenced after the cloud proof (S13.101), and no code duplicates an invocation or charges a
/// shadow budget. The slot exists so enabling it later is an additive transition rather than a
/// rename of a shipped value — and a shadow version never becomes <c>current</c>, so it can never
/// serve or trip <see cref="RoleCatalogueCheck"/>.
/// </para>
/// </summary>
public sealed class MappingProposalState : SmartEnum<MappingProposalState>
{
    /// <summary>Recorded, awaiting a decision by a principal holding <c>model-governance</c>.</summary>
    public static readonly MappingProposalState PendingApproval = new("pending_approval");

    /// <summary>Reserved for shadow evaluation (doc 08 §7). Never entered in this release.</summary>
    public static readonly MappingProposalState Shadow = new("shadow");

    /// <summary>Approved: its mapping version was allocated and is live in the canary ring.</summary>
    public static readonly MappingProposalState Approved = new("approved");

    /// <summary>Promoted to the fleet ring by a manual decision (auto-promotion is not built).</summary>
    public static readonly MappingProposalState Promoted = new("promoted");

    /// <summary>Refused by an approver. Terminal.</summary>
    public static readonly MappingProposalState Rejected = new("rejected");

    /// <summary>Retracted by its proposer before a decision. Terminal.</summary>
    public static readonly MappingProposalState Withdrawn = new("withdrawn");

    /// <summary>Its live mapping was rolled back to an earlier version. Terminal.</summary>
    public static readonly MappingProposalState RolledBack = new("rolled_back");

    /// <summary>
    /// The legal moves out of each state. Keyed by <see cref="SmartEnum{TEnum}.Name"/> rather than by
    /// instance so the table can be built before every value's static initialiser has run.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedTransitions = new(StringComparer.Ordinal)
    {
        ["pending_approval"] = ["shadow", "approved", "rejected", "withdrawn"],
        ["shadow"] = ["approved", "rejected", "withdrawn"],
        ["approved"] = ["promoted", "rolled_back"],
        ["promoted"] = ["rolled_back"],
        ["rejected"] = [],
        ["withdrawn"] = [],
        ["rolled_back"] = [],
    };

    private MappingProposalState(string name)
        : base(name)
    {
    }

    /// <summary>Whether no decision may move this proposal any further.</summary>
    public bool IsTerminal => AllowedTransitions[Name].Length == 0;

    /// <summary>
    /// The states that hold a role's single proposal slot: a change request someone still has to
    /// decide (Mark's call, 2026-09-16). <see cref="Shadow"/> is listed now and entered later.
    /// </summary>
    private static readonly string[] AwaitsDecision = ["pending_approval", "shadow"];

    /// <summary>
    /// The states that free the role to take a new proposal, named explicitly and deliberately
    /// <b>not</b> derived from <see cref="IsTerminal"/>. <see cref="Approved"/> and
    /// <see cref="Promoted"/> are non-terminal, because a rollback can still move them, yet they have
    /// been decided — so a role whose mapping reached fleet must obviously still accept the next
    /// proposal. Deriving this set from terminality would have locked such a role out until someone
    /// rolled it back.
    /// </summary>
    private static readonly string[] ReleasesSlot = ["approved", "promoted", "rejected", "withdrawn", "rolled_back"];

    /// <summary>
    /// Whether this proposal is still awaiting a decision, and so holds its role's single proposal
    /// slot. The rule exists so an approver never has to work out which of several competing
    /// undecided proposals wins; a decided proposal is no longer competing.
    /// </summary>
    public bool IsAwaitingDecision => Array.IndexOf(AwaitsDecision, Name) >= 0;

    /// <summary>
    /// Whether a proposal in this state leaves its role free to take a new one. The exact complement
    /// of <see cref="IsAwaitingDecision"/> over the declared values, which a test pins.
    /// </summary>
    public bool ReleasesRoleProposalSlot => Array.IndexOf(ReleasesSlot, Name) >= 0;

    /// <summary>Whether a proposal in this state may legally move to <paramref name="next"/>.</summary>
    public bool CanTransitionTo(MappingProposalState next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return Array.IndexOf(AllowedTransitions[Name], next.Name) >= 0;
    }
}
