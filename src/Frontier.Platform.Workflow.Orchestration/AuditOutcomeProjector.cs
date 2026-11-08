using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// How a run that did not complete is told in its audit record (S13.240, ADR-PA46): the snapshot's
/// failure fields become <see cref="FailureRecord"/>, its termination fields
/// <see cref="TerminationRecord"/>, and a record attesting <see cref="ExecutionStatus.Failed"/> or
/// <see cref="ExecutionStatus.Terminated"/> without them is refused before it is signed.
/// </summary>
internal static class AuditOutcomeProjector
{
    /// <summary>The failure <paramref name="snapshot"/> recorded, or <see langword="null"/> when it recorded none.</summary>
    internal static FailureRecord? Failure(ExecutionSnapshot snapshot) => snapshot.FailureClassification is { } classification
        ? new FailureRecord
        {
            Classification = classification,
            NodeId = snapshot.FailureDetail?.NodeId,
            ExceptionType = snapshot.FailureDetail?.ExceptionType,
            Message = snapshot.FailureDetail?.Message,
        }
        : null;

    /// <summary>Who terminated the run, why and when, or <see langword="null"/> unless <paramref name="snapshot"/> recorded all three.</summary>
    internal static TerminationRecord? Termination(ExecutionSnapshot snapshot) =>
        snapshot is { TerminatedBy: { } terminatedBy, TerminationReason: { } reason, TerminatedAtUtc: { } terminatedAtUtc }
            ? new TerminationRecord { TerminatedBy = terminatedBy, Reason = reason, TerminatedAtUtc = terminatedAtUtc }
            : null;

    /// <summary>
    /// Returns <paramref name="record"/>, or throws a permanent <see cref="ContractViolationException"/>
    /// when it attests failed or terminated without saying why. The usual cause is a terminate that
    /// consolidated before writing its terminated snapshot: the record would chain and sign a claim
    /// with nothing behind it, and audit-records is append-only.
    /// </summary>
    internal static AuditRecord EnsureEvidenced(AuditRecord record)
    {
        if (record.FinalStatus == ExecutionStatus.Failed && record.Failure is null)
        {
            throw new ContractViolationException(nameof(AuditRecord), [$"Execution '{record.ExecutionId}' is attested failed, but its latest snapshot records no failure_classification."]);
        }

        if (record.FinalStatus == ExecutionStatus.Terminated && record.Termination is null)
        {
            throw new ContractViolationException(nameof(AuditRecord), [$"Execution '{record.ExecutionId}' is attested terminated, but its latest snapshot does not record terminated_by, termination_reason and terminated_at_utc; write the terminated snapshot before consolidating."]);
        }

        return record;
    }
}
