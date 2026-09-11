namespace Frontier.Platform.Guardrails;

/// <summary>
/// Pure accumulation of one <see cref="UsageRecord"/> into the <c>guardrail-ledger</c> document shape
/// (doc 07 §6), kept apart from <see cref="CosmosBudgetLedger"/>'s I/O so both the engagement totals and
/// the per-execution snapshot stay current on every write, not only on the document's first.
/// </summary>
internal static class BudgetLedgerAccumulation
{
    /// <summary>
    /// The ledger document for <paramref name="usage"/>'s engagement after recording it: a fresh document when
    /// <paramref name="existing"/> is <c>null</c>, otherwise its totals plus the usage. Usage in a currency other
    /// than the document's is refused (ADR-PA21). Never mutates <paramref name="existing"/>.
    /// </summary>
    internal static BudgetLedgerDocument Accumulate(BudgetLedgerDocument? existing, UsageRecord usage, DateTime nowUtc)
    {
        var document = existing ?? Empty(usage);
        CostCurrency.EnsureCombinable(nameof(BudgetLedgerDocument), existing?.Currency, usage.Currency);

        var snapshots = new Dictionary<string, ExecutionLedgerSnapshot>(document.ExecutionSnapshots ?? [], StringComparer.Ordinal);
        snapshots[usage.ExecutionId] = AccumulateExecution(snapshots.GetValueOrDefault(usage.ExecutionId), usage, nowUtc);

        return document with
        {
            TotalInputTokens = document.TotalInputTokens + usage.InputTokens,
            TotalOutputTokens = document.TotalOutputTokens + usage.OutputTokens,
            TotalCost = document.TotalCost + usage.Cost,
            InvocationCount = document.InvocationCount + 1,
            ExecutionSnapshots = snapshots,
        };
    }

    /// <summary>
    /// <paramref name="usage"/>'s execution snapshot after recording it: a fresh snapshot when
    /// <paramref name="existing"/> is <c>null</c>, otherwise its tokens (input + output), cost and invocation
    /// count plus the usage, stamped <paramref name="nowUtc"/>. Usage in a currency other than the snapshot's
    /// is refused (ADR-PA21).
    /// </summary>
    internal static ExecutionLedgerSnapshot AccumulateExecution(ExecutionLedgerSnapshot? existing, UsageRecord usage, DateTime nowUtc)
    {
        CostCurrency.EnsureCombinable(nameof(ExecutionLedgerSnapshot), existing?.Currency, usage.Currency);

        return new ExecutionLedgerSnapshot(
            ExecutionId: usage.ExecutionId,
            TotalTokens: (existing?.TotalTokens ?? 0) + usage.InputTokens + usage.OutputTokens,
            TotalCost: (existing?.TotalCost ?? 0m) + usage.Cost,
            Currency: usage.Currency,
            InvocationCount: (existing?.InvocationCount ?? 0) + 1,
            LastUpdatedUtc: nowUtc);
    }

    /// <summary>An engagement ledger document with nothing recorded yet, in <paramref name="usage"/>'s currency.</summary>
    internal static BudgetLedgerDocument Empty(UsageRecord usage) => new()
    {
        PartitionKey = usage.EngagementId,
        Id = DocumentId(usage.EngagementId),
        EngagementId = usage.EngagementId,
        Currency = usage.Currency,
    };

    /// <summary>The ledger document id for <paramref name="engagementId"/>: <c>{engagementId}:ledger</c>.</summary>
    internal static string DocumentId(string engagementId) => $"{engagementId}:ledger";
}
