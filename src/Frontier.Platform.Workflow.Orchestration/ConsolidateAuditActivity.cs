using Frontier.Platform.Audit;
using Microsoft.DurableTask;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// <c>ConsolidateAuditActivity</c> (doc 05 §8, S5.6): the orchestrator's final activity on
/// full-graph completion. Delegates to <see cref="IAuditConsolidator"/> to build the unsigned
/// <see cref="AuditRecord"/> from the just-written final <see cref="ExecutionSnapshot"/> and
/// staged telemetry, then to <see cref="IAuditSigner"/> to chain, sign, and persist it to
/// <c>audit-records</c>. <see cref="TaskActivityContext"/> carries no <see cref="CancellationToken"/>;
/// <see cref="CancellationToken.None"/> is the established convention for DTF activities in
/// this codebase.
/// </summary>
[DurableTask(WorkflowActivityNames.ConsolidateAuditActivity)]
public sealed class ConsolidateAuditActivity(IAuditConsolidator consolidator, IAuditSigner signer) : TaskActivity<ConsolidateAuditInput, SignedAuditRecord>
{
    /// <inheritdoc />
    public override async Task<SignedAuditRecord> RunAsync(TaskActivityContext context, ConsolidateAuditInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var record = await consolidator.ConsolidateAsync(input, CancellationToken.None);
        return await signer.SignAsync(record, CancellationToken.None);
    }
}
