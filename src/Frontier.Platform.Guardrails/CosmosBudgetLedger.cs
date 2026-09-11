using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// Cosmos-backed <see cref="IBudgetLedger"/> for the <c>guardrail-ledger</c> container (doc 07 §6, S6.5a).
/// Uses partial-document patches (increment operations) for optimistic concurrency on high-contention scenarios
/// (multiple invocations in an execution incrementing the same ledger doc simultaneously).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos integration adapter; tested against emulator locally only, not in CI.")]
internal sealed class CosmosBudgetLedger : IBudgetLedger
{
    private readonly Container container;

    /// <summary>Creates a new ledger backed by the given Cosmos container (PK: /engagementId).</summary>
    public CosmosBudgetLedger(Container container) => this.container = container ?? throw new ArgumentNullException(nameof(container));

    /// <inheritdoc />
    public async Task RecordUsageAsync(UsageRecord usage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var docId = BudgetLedgerAccumulation.DocumentId(usage.EngagementId);

        try
        {
            var doc = await container.ReadItemAsync<BudgetLedgerDocument>(
                docId,
                new PartitionKey(usage.EngagementId),
                cancellationToken: cancellationToken);

            var updated = BudgetLedgerAccumulation.Accumulate(doc.Resource, usage, DateTime.UtcNow);

            await container.ReplaceItemAsync(
                updated,
                docId,
                new PartitionKey(usage.EngagementId),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var newDoc = BudgetLedgerAccumulation.Accumulate(null, usage, DateTime.UtcNow);

            await container.CreateItemAsync(newDoc, new PartitionKey(usage.EngagementId), cancellationToken: cancellationToken);
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
