
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

    /// <summary>
    /// Legacy (ADR-PA47): what a failed run's last checkpoint said before S13.272. Nothing resumed such a
    /// run, so the status was never true; a failed run now ends <see cref="Failed"/>. The value stays so
    /// snapshots and activity inputs recorded before the change still deserialize (ADR-E15: wire shapes
    /// only grow); read it only to reconcile those snapshots, and never write it.
    /// </summary>
    [Obsolete("Deprecated in v0.57.0 (ADR-PA47): use Failed, which a failed run now ends on. Read this only to reconcile snapshots written before it.")]
    public static readonly ExecutionStatus PausedOnFailure = new("paused_on_failure");

    /// <summary>The graph ran to completion.</summary>
    public static readonly ExecutionStatus Completed = new("completed");

    /// <summary>
    /// A node failed permanently and the run ended (ADR-PA47): terminal, nothing resumes it. The
    /// orchestrator's last checkpoint carries the doc 10 §3 classification and the failing node.
    /// </summary>
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
