using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// Cosmos-backed <see cref="IBudgetLedger"/> for the <c>guardrail-ledger</c> container (doc 07 §6, S6.5a),
/// partitioned on <c>/engagement_id</c>. Each usage record is a read-accumulate-write of the engagement's single
/// ledger document under ETag optimistic concurrency: a write that loses a race to a concurrent one (412 on
/// replace, 409 on first create) backs off with jitter and starts again from the read, up to <see cref="MaxWriteAttempts"/> attempts.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter; covered by the emulator integration tests in CI's integration job, which the unit coverage gate does not run.")]
internal sealed class CosmosBudgetLedger : IBudgetLedger
{
    /// <summary>How many read-accumulate-write attempts a usage record gets before the last conflict is rethrown.</summary>
    internal const int MaxWriteAttempts = 10;

    private readonly Container container;

    /// <summary>Creates a new ledger backed by the given Cosmos container (PK: /engagement_id).</summary>
    public CosmosBudgetLedger(Container container) => this.container = container ?? throw new ArgumentNullException(nameof(container));

    /// <inheritdoc />
    /// <remarks>
    /// When every attempt loses to a concurrent write, the last conflict <see cref="CosmosException"/> is rethrown so
    /// the caller's resilience policy sees a transient failure.
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
                var ceiling = BudgetLedgerWriteConflict.BackoffCeiling(attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, (int)ceiling.TotalMilliseconds + 1)), cancellationToken);
            }
        }
    }

    /// <summary>One read-accumulate-write attempt; throws a 412/409 <see cref="CosmosException"/> on a lost race.</summary>
    private async Task TryRecordUsageAsync(UsageRecord usage, CancellationToken cancellationToken)
    {
        var docId = BudgetLedgerAccumulation.DocumentId(usage.EngagementId);
        var partitionKey = new PartitionKey(usage.EngagementId);
        var (existing, etag) = await ReadLedgerAsync(docId, partitionKey, cancellationToken);
        var updated = BudgetLedgerAccumulation.Accumulate(existing, usage, DateTime.UtcNow);

        if (existing is null)
        {
            await container.CreateItemAsync(updated, partitionKey, cancellationToken: cancellationToken);
            return;
        }

        await container.ReplaceItemAsync(
            updated,
            docId,
            partitionKey,
            new ItemRequestOptions { IfMatchEtag = etag },
            cancellationToken);
    }

    /// <summary>Reads the ledger document with its ETag; a missing document yields <c>(null, null)</c>.</summary>
    private async Task<(BudgetLedgerDocument? Document, string? ETag)> ReadLedgerAsync(string docId, PartitionKey partitionKey, CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<BudgetLedgerDocument>(docId, partitionKey, cancellationToken: cancellationToken);
            return (response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return (null, null);
        }
    }

    /// <inheritdoc />
    public async Task<BudgetSnapshot> GetSnapshotAsync(BudgetScopeRef scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        switch (scope.Kind)
        {
            case BudgetScopeKind.Invocation:
                return await GetInvocationSnapshotAsync(scope.Id, cancellationToken);

            case BudgetScopeKind.Execution:
                return await GetExecutionSnapshotAsync(scope.Id, cancellationToken);

            case BudgetScopeKind.Engagement:
                return await GetEngagementSnapshotAsync(scope.Id, cancellationToken);

            case BudgetScopeKind.Fleet:
                return new BudgetSnapshot(scope, 0, 0, null, 0);

            default:
                throw new InvalidOperationException($"Unknown budget scope kind: {scope.Kind}");
        }
    }

    /// <summary>
    /// Queries for invocation by correlationId (not stored in ledger doc; requires separate invocation-usage table or log).
    /// Phase 1 limitation: defer to S6.5b (invocation-level ledger table with separate PK).
    /// </summary>
    private static Task<BudgetSnapshot> GetInvocationSnapshotAsync(string correlationId, CancellationToken cancellationToken)
    {
        var scope = new BudgetScopeRef(BudgetScopeKind.Invocation, correlationId);
        return Task.FromResult(new BudgetSnapshot(scope, 0, 0, null, 0));
    }

    /// <summary>Retrieves execution-level usage from the ledger doc's ExecutionSnapshots map.</summary>
    private async Task<BudgetSnapshot> GetExecutionSnapshotAsync(string executionId, CancellationToken cancellationToken)
    {
        // SQL, not LINQ: the Cosmos LINQ provider cannot translate Dictionary.ContainsKey.
        var query = new QueryDefinition("SELECT * FROM c WHERE IS_DEFINED(c.execution_snapshots[@executionId])")
            .WithParameter("@executionId", executionId);
        using var docs = container.GetItemQueryIterator<BudgetLedgerDocument>(query);

        while (docs.HasMoreResults)
        {
            var page = await docs.ReadNextAsync(cancellationToken);
            foreach (var doc in page)
            {
                if (doc.ExecutionSnapshots?.TryGetValue(executionId, out var snapshot) == true)
                {
                    var scope = new BudgetScopeRef(BudgetScopeKind.Execution, executionId);
                    return new BudgetSnapshot(scope, snapshot.TotalTokens, snapshot.TotalCost, snapshot.Currency, snapshot.InvocationCount);
                }
            }
        }

        var emptyScope = new BudgetScopeRef(BudgetScopeKind.Execution, executionId);
        return new BudgetSnapshot(emptyScope, 0, 0, null, 0);
    }

    /// <summary>Retrieves engagement-level usage (totals from the ledger doc).</summary>
    private async Task<BudgetSnapshot> GetEngagementSnapshotAsync(string engagementId, CancellationToken cancellationToken)
    {
        try
        {
            var docId = $"{engagementId}:ledger";
            var doc = await container.ReadItemAsync<BudgetLedgerDocument>(
                docId,
                new PartitionKey(engagementId),
                cancellationToken: cancellationToken);

            var scope = new BudgetScopeRef(BudgetScopeKind.Engagement, engagementId);
            return new BudgetSnapshot(
                scope,
                doc.Resource.TotalInputTokens + doc.Resource.TotalOutputTokens,
                doc.Resource.TotalCost,
                doc.Resource.Currency,
                doc.Resource.InvocationCount);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var scope = new BudgetScopeRef(BudgetScopeKind.Engagement, engagementId);
            return new BudgetSnapshot(scope, 0, 0, null, 0);
        }
    }
}
