namespace Frontier.Platform.Guardrails;

/// <summary>
/// Attribution-complete usage metering (doc 07 §2 rule 7, §6). The invocation pipeline
/// (S4.2) calls <see cref="RecordUsageAsync"/> after every MAF call; Observability and
/// the audit trail slice <see cref="GetSnapshotAsync"/> by scope.
/// </summary>
public interface IBudgetLedger
{
    /// <summary>Records actual usage, idempotent on <see cref="UsageRecord.CorrelationId"/> (doc 07 §6 — an activity retry must not double-count).</summary>
    Task RecordUsageAsync(UsageRecord usage, CancellationToken cancellationToken);

    /// <summary>Returns the aggregated usage for <paramref name="scope"/>.</summary>
    Task<BudgetSnapshot> GetSnapshotAsync(BudgetScopeRef scope, CancellationToken cancellationToken);
}
