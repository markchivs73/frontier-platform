namespace Frontier.Platform.Observability;

/// <summary>
/// Categories of doc 12 §8 recovery-sweep findings recorded via
/// <see cref="IRecoveryFindingsRecorder"/> (C-22). A pure structural discriminator with
/// no behaviour — a standard enum per engineering-standards.
/// </summary>
public enum RecoveryFindingType
{
    /// <summary>An <c>execution-snapshots</c> projection had a stale <c>is_latest</c> flag that was demoted back to a single latest checkpoint.</summary>
    IsLatestHealed,

    /// <summary>A decided approval's gate-decision event was lost and re-raised because the execution was still paused at that gate (C-21).</summary>
    GateReraised,

    /// <summary>A terminally-statused execution had no <c>audit-records</c> document (C-25 detection-only descope).</summary>
    AuditMissing,

    /// <summary>
    /// An execution's DTF instance permanently faulted but its projection was still
    /// <c>running</c>/<c>paused_at_gate</c> — healed to <c>paused_on_failure</c> with a
    /// real doc 10 §3 reason code (S9.45, doc 03 §9/§10, doc 19 §B3).
    /// </summary>
    FailedExecutionHealed,
}
