
namespace Frontier.Platform.Abstractions;

/// <summary>
/// Lifecycle status of an <c>ExecutionSnapshot</c> (doc 02 §2). Serializes as a
/// snake_case string, identical to a standard enum (doc 00 §3.5).
/// </summary>
public sealed class ExecutionStatus : SmartEnum<ExecutionStatus>
{
    /// <summary>The orchestration is actively walking the graph.</summary>
    public static readonly ExecutionStatus Running = new("running");

    /// <summary>Suspended awaiting a <c>HumanGateNode</c> decision.</summary>
    public static readonly ExecutionStatus PausedAtGate = new("paused_at_gate");

    /// <summary>Suspended after a permanent step failure, awaiting resolution (doc 03 §9).</summary>
    public static readonly ExecutionStatus PausedOnFailure = new("paused_on_failure");

    /// <summary>The graph ran to completion.</summary>
    public static readonly ExecutionStatus Completed = new("completed");

    /// <summary>The execution terminated with an unrecoverable error.</summary>
    public static readonly ExecutionStatus Failed = new("failed");

    /// <summary>The execution was cancelled before completion.</summary>
    public static readonly ExecutionStatus Cancelled = new("cancelled");

    /// <summary>
    /// The live run was ended through the governed terminate (S13.149, doc 20): terminal, and the
    /// snapshot says who, why and when. The actor is the person who asked, or a <c>system:*</c> actor
    /// when a machine terminated it (ADR-E8). Distinct from <see cref="Cancelled"/>, which records no
    /// actor and no reason.
    /// </summary>
    public static readonly ExecutionStatus Terminated = new("terminated");

    private ExecutionStatus(string name)
        : base(name)
    {
    }
}
