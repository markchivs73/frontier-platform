using System.Collections.Concurrent;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// In-process <see cref="IBudgetLedger"/> (doc 07 §6 "or in-memory for PoC", S4.5). One
/// worker's in-memory map is sufficient for the PoC Gate 3 single-worker harness;
/// the Cosmos-backed <c>guardrail-ledger</c> container (PK <c>/engagementId</c>,
/// partial-document patch increments, change-feed fleet aggregation) is S6.5.
/// </summary>
internal sealed class BudgetLedger : IBudgetLedger
{
    private readonly ConcurrentDictionary<string, UsageRecord> recordsByCorrelationId = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task RecordUsageAsync(UsageRecord usage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usage);

        recordsByCorrelationId.TryAdd(usage.CorrelationId, usage);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<BudgetSnapshot> GetSnapshotAsync(BudgetScopeRef scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var matching = recordsByCorrelationId.Values.Where(record => Matches(record, scope)).ToList();
        return Task.FromResult(new BudgetSnapshot(
            scope,
            matching.Sum(record => record.InputTokens + record.OutputTokens),
            matching.Sum(record => record.CostGbp),
            matching.Count));
    }

    /// <summary>Whether <paramref name="record"/> belongs to <paramref name="scope"/> (doc 07 §6 — <see cref="BudgetScopeKind.Fleet"/> is never matched here; it's aggregated asynchronously, S6.5).</summary>
    internal static bool Matches(UsageRecord record, BudgetScopeRef scope) => scope.Kind switch
    {
        BudgetScopeKind.Invocation => record.CorrelationId == scope.Id,
        BudgetScopeKind.Execution => record.ExecutionId == scope.Id,
        BudgetScopeKind.Engagement => record.EngagementId == scope.Id,
        _ => false,
    };
}
