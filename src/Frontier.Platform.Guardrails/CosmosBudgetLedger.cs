using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// Cosmos-backed <see cref="IBudgetLedger"/> for the <c>guardrail-ledger</c> container (doc 07 §6, S6.5a),
/// partitioned on <c>/engagement_id</c>. Each usage record is a read-accumulate-write of the engagement's single
/// ledger document under ETag optimistic concurrency: a write that loses a race to a concurrent one (412 on
/// replace, 409 on first create) backs off with jitter and starts again from the read, up to <see cref="MaxWriteAttempts"/> attempts.
/// </summary>
/// <remarks>
/// The decisions — the retry loop, the backoff, which document answers which scope — live here and are unit-tested;
/// the SDK calls are <see cref="CosmosBudgetLedgerDocuments"/> (S13.193).
/// </remarks>
internal sealed class CosmosBudgetLedger : IBudgetLedger
{
    /// <summary>
    /// How many read-accumulate-write attempts a usage record gets before the last conflict is rethrown: one per writer
    /// for 25 concurrent writers on one ledger (S13.233). Each lost attempt means another writer's write committed
    /// between this attempt's read and its write, and the next attempt reads afterwards, so every loss is to a different
    /// write. When each read sees the latest committed document, a record contending with 24 others that each write
    /// once loses at most 24 times and the 25th attempt lands, whatever the timing. The backoff one record can spend
    /// before that last attempt stays under 20 seconds.
    /// </summary>
    internal const int MaxWriteAttempts = 25;

    private readonly IBudgetLedgerDocuments documents;
    private readonly Func<int, int> jitterMilliseconds;

    /// <summary>Creates a new ledger backed by the given Cosmos container (PK: /engagement_id).</summary>
    public CosmosBudgetLedger(Container container)
        : this(new CosmosBudgetLedgerDocuments(container), FullJitterMilliseconds)
    {
    }

    /// <summary>
    /// Creates a ledger over <paramref name="documents"/>, drawing each backoff from <paramref name="jitterMilliseconds"/>
    /// (given the ceiling in milliseconds). A test seam: production passes <see cref="FullJitterMilliseconds"/>; tests pass a
    /// deterministic draw so the backoff ceilings can be asserted.
    /// </summary>
    internal CosmosBudgetLedger(IBudgetLedgerDocuments documents, Func<int, int> jitterMilliseconds)
    {
        this.documents = documents ?? throw new ArgumentNullException(nameof(documents));
        this.jitterMilliseconds = jitterMilliseconds ?? throw new ArgumentNullException(nameof(jitterMilliseconds));
    }

    /// <summary>Full jitter: a uniformly random delay from zero to <paramref name="ceilingMilliseconds"/> inclusive.</summary>
    internal static int FullJitterMilliseconds(int ceilingMilliseconds) =>
        System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, ceilingMilliseconds + 1);

    /// <inheritdoc />
    /// <remarks>
    /// When every attempt loses to a concurrent write, the last conflict <see cref="CosmosException"/> (412 or 409) is
    /// rethrown. The platform's failure classifier treats it as permanent, so a Polly inner loop gated by the classifier
    /// does not retry it; an outer DTF activity retry, which retries every failure but a contract, budget or suspension
    /// one, can still run the caller's whole activity again. Within one call, <see cref="MaxWriteAttempts"/> is the
    /// whole budget.
    /// </remarks>
    public async Task RecordUsageAsync(UsageRecord usage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usage);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await TryRecordUsageAsync(usage, cancellationToken);
                return;
            }
            catch (CosmosException ex) when (BudgetLedgerWriteConflict.IsConcurrencyConflict(ex.StatusCode) && attempt < MaxWriteAttempts)
            {
                // Lost the race to a concurrent write: back off with jitter so contenders spread out, then re-read.
                var ceiling = (int)BudgetLedgerWriteConflict.BackoffCeiling(attempt).TotalMilliseconds;
                await Task.Delay(TimeSpan.FromMilliseconds(jitterMilliseconds(ceiling)), cancellationToken);
            }
        }
    }

    /// <summary>One read-accumulate-write attempt; throws a 412/409 <see cref="CosmosException"/> on a lost race.</summary>
    private async Task TryRecordUsageAsync(UsageRecord usage, CancellationToken cancellationToken)
    {
        var (existing, etag) = await documents.ReadAsync(usage.EngagementId, cancellationToken);
        var updated = BudgetLedgerAccumulation.Accumulate(existing, usage, DateTime.UtcNow);

        await (existing is null
            ? documents.CreateAsync(updated, cancellationToken)
            : documents.ReplaceAsync(updated, etag, cancellationToken));
    }

    /// <inheritdoc />
    public Task<BudgetSnapshot> GetSnapshotAsync(BudgetScopeRef scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return scope.Kind switch
        {
            // Invocation-level usage is not stored in the ledger doc; Phase 1 limitation, deferred to S6.5b.
            BudgetScopeKind.Invocation or BudgetScopeKind.Fleet => Task.FromResult(Empty(scope)),
            BudgetScopeKind.Execution => GetExecutionSnapshotAsync(scope, cancellationToken),
            BudgetScopeKind.Engagement => GetEngagementSnapshotAsync(scope, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown budget scope kind: {scope.Kind}"),
        };
    }

    /// <summary>Execution-level usage: the first ledger document holding a snapshot for the execution, else empty.</summary>
    private async Task<BudgetSnapshot> GetExecutionSnapshotAsync(BudgetScopeRef scope, CancellationToken cancellationToken)
    {
        await foreach (var doc in documents.FindByExecutionAsync(scope.Id, cancellationToken))
        {
            if (doc.ExecutionSnapshots?.TryGetValue(scope.Id, out var snapshot) == true)
            {
                return new BudgetSnapshot(scope, snapshot.TotalTokens, snapshot.TotalCost, snapshot.Currency, snapshot.InvocationCount);
            }
        }

        return Empty(scope);
    }

    /// <summary>Engagement-level usage: the totals of the engagement's ledger document, else empty.</summary>
    private async Task<BudgetSnapshot> GetEngagementSnapshotAsync(BudgetScopeRef scope, CancellationToken cancellationToken)
    {
        var (doc, _) = await documents.ReadAsync(scope.Id, cancellationToken);
        return doc is null
            ? Empty(scope)
            : new BudgetSnapshot(scope, doc.TotalInputTokens + doc.TotalOutputTokens, doc.TotalCost, doc.Currency, doc.InvocationCount);
    }

    /// <summary>Nothing recorded at <paramref name="scope"/>.</summary>
    internal static BudgetSnapshot Empty(BudgetScopeRef scope) => new(scope, 0, 0, null, 0);
}
