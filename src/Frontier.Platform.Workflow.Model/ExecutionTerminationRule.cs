using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// The <see cref="ExecutionSnapshot.Validate"/> rule for a terminated run (S13.149, doc 20): a
/// <see cref="ExecutionStatus.Terminated"/> snapshot says who ended the run, why and when, as a
/// <see cref="ExecutionStatus.PausedOnFailure"/> one must say what kind of failure paused it. A snapshot
/// in any other status is not checked, so every snapshot written before the fields existed still validates.
/// <para>
/// There is no exemption for a terminate no person asked for: it must still give an actor, a reason and a
/// time. By convention (ADR-E8) a machine-originated terminate — a recovery sweep, say — names an explicit
/// <c>system:*</c> actor such as <c>system:recovery</c>. This rule cannot check that convention: a human
/// principal id has no fixed format, so a non-blank actor is all it can require. The consumer's writer is
/// what keeps it.
/// </para>
/// </summary>
internal static class ExecutionTerminationRule
{
    /// <summary>The violations <paramref name="snapshot"/> has under this rule; none unless its status is terminated.</summary>
    internal static IEnumerable<string> Violations(ExecutionSnapshot snapshot)
    {
        if (snapshot.Status != ExecutionStatus.Terminated)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(snapshot.TerminatedBy))
        {
            yield return "terminated_by is required when status is terminated.";
        }

        if (string.IsNullOrWhiteSpace(snapshot.TerminationReason))
        {
            yield return "termination_reason is required when status is terminated.";
        }

        if (snapshot.TerminatedAtUtc is null)
        {
            yield return "terminated_at_utc is required when status is terminated.";
        }
    }
}
