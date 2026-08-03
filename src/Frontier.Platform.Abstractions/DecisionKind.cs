
namespace Frontier.Platform.Abstractions;

/// <summary>
/// The outcome of a <see cref="HitlDecision"/> at a <c>HumanGateNode</c> (doc 06 §3).
/// Serializes as a snake_case string, identical to a standard enum (doc 00 §3.5).
/// </summary>
public sealed class DecisionKind : SmartEnum<DecisionKind>
{
    /// <summary>The approver accepted the section(s) shown at the gate.</summary>
    public static readonly DecisionKind Approve = new("approve");

    /// <summary>The approver rejected the section(s); rollback/regeneration follows per the gate's configuration.</summary>
    public static readonly DecisionKind Reject = new("reject");

    /// <summary>The approver escalated the decision to another role.</summary>
    public static readonly DecisionKind Escalate = new("escalate");

    private DecisionKind(string name)
        : base(name)
    {
    }
}
