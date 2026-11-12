using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// The <see cref="ExecutionSnapshot.Validate"/> rule for a run that ended on a failure (ADR-PA47): a
/// <see cref="ExecutionStatus.Failed"/> snapshot, or one still carrying the legacy <c>paused_on_failure</c>
/// written before S13.272, must say what kind of failure ended it.
/// </summary>
internal static class ExecutionFailureRule
{
    /// <summary>Whether a snapshot in <paramref name="status"/> must carry a failure classification: <c>failed</c> or the legacy <c>paused_on_failure</c>.</summary>
    internal static bool RequiresClassification(ExecutionStatus status) =>
#pragma warning disable CS0618 // Old snapshots still carry the legacy status, and the rule still holds for them (ADR-PA47).
        status == ExecutionStatus.Failed || status == ExecutionStatus.PausedOnFailure;
#pragma warning restore CS0618
}
