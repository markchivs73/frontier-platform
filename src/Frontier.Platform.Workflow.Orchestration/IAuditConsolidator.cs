
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Consolidates one execution's evidence into an unsigned <see cref="AuditRecord"/> (doc 05
/// §2, §4). The audit signer (S5.5) signs and chains the result before it is persisted to
/// <c>audit-records</c>.
/// <para>
/// <b>Closing a terminated run (S13.240, ADR-PA46).</b> A terminated orchestration runs no further
/// activities, so whoever terminates it closes its record outside the orchestrator: after the DTF
/// terminate, write the <c>terminated</c> snapshot (with <c>terminated_by</c>,
/// <c>termination_reason</c> and <c>terminated_at_utc</c>), then call <see cref="ConsolidateAsync"/>
/// with the run's identity, its pinned <c>definition_hash</c> and start, and
/// <see cref="ConsolidateAuditInput.FinalStatus"/> set to <c>terminated</c> (or null, which takes the
/// snapshot's), and hand the result to <see cref="IAuditSigner.SignAsync"/>. A <c>failed</c> or
/// <c>terminated</c> claim the latest snapshot does not back is refused with a
/// <see cref="Frontier.Platform.Abstractions.ContractViolationException"/>.
/// </para>
/// </summary>
public interface IAuditConsolidator
{
    /// <summary>Builds the <see cref="AuditRecord"/> for <paramref name="input"/>'s execution.</summary>
    Task<AuditRecord> ConsolidateAsync(ConsolidateAuditInput input, CancellationToken cancellationToken);
}
