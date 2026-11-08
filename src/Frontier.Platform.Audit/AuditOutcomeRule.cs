using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// The <c>Validate()</c> rule shared by <see cref="AuditRecord"/> and <see cref="SignedAuditRecord"/>
/// (S13.240, ADR-PA46): a record whose final status is <see cref="ExecutionStatus.Failed"/> says why it
/// failed, and one whose final status is <see cref="ExecutionStatus.Terminated"/> says who ended it. No
/// other status is checked, so every record written before the members existed still validates.
/// </summary>
internal static class AuditOutcomeRule
{
    /// <summary>The violations a record with these members has under this rule.</summary>
    internal static IEnumerable<string> Violations(ExecutionStatus finalStatus, FailureRecord? failure, TerminationRecord? termination)
    {
        if (finalStatus == ExecutionStatus.Failed && string.IsNullOrWhiteSpace(failure?.Classification))
        {
            yield return "failure with a classification is required when final_status is failed.";
        }

        if (finalStatus == ExecutionStatus.Terminated && (string.IsNullOrWhiteSpace(termination?.TerminatedBy) || string.IsNullOrWhiteSpace(termination.Reason)))
        {
            yield return "termination with terminated_by and reason is required when final_status is terminated.";
        }
    }
}
