
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Consolidates one execution's evidence into an unsigned <see cref="AuditRecord"/> (doc 05
/// §2, §4). The audit signer (S5.5) signs and chains the result before it is persisted to
/// <c>audit-records</c>.
/// </summary>
public interface IAuditConsolidator
{
    /// <summary>Builds the <see cref="AuditRecord"/> for <paramref name="input"/>'s execution.</summary>
    Task<AuditRecord> ConsolidateAsync(ConsolidateAuditInput input, CancellationToken cancellationToken);
}
