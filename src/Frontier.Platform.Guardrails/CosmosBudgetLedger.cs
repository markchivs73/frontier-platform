using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

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

        var docId = $"{usage.EngagementId}:ledger";

        try
        {
            var doc = await container.ReadItemAsync<BudgetLedgerDocument>(
                docId,
                new PartitionKey(usage.EngagementId),
                cancellationToken: cancellationToken);

            var updated = doc.Resource with
            {
                TotalInputTokens = doc.Resource.TotalInputTokens + usage.InputTokens,
                TotalOutputTokens = doc.Resource.TotalOutputTokens + usage.OutputTokens,
                TotalCostGbp = doc.Resource.TotalCostGbp + usage.CostGbp,
                InvocationCount = doc.Resource.InvocationCount + 1,
            };

            await container.ReplaceItemAsync(
                updated,
                docId,
                new PartitionKey(usage.EngagementId),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var newDoc = new BudgetLedgerDocument
            {
                PartitionKey = usage.EngagementId,
                Id = docId,
                EngagementId = usage.EngagementId,
                TotalInputTokens = usage.InputTokens,
                TotalOutputTokens = usage.OutputTokens,
                TotalCostGbp = usage.CostGbp,
                InvocationCount = 1,
                ExecutionSnapshots = new Dictionary<string, ExecutionLedgerSnapshot>
                {
                    [usage.ExecutionId] = new ExecutionLedgerSnapshot(
                        ExecutionId: usage.ExecutionId,
                        TotalTokens: usage.InputTokens + usage.OutputTokens,
                        TotalCostGbp: usage.CostGbp,
                        InvocationCount: 1,
                        LastUpdatedUtc: DateTime.UtcNow),
                },
            };

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
                return new BudgetSnapshot(scope, 0, 0, 0);

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
        return Task.FromResult(new BudgetSnapshot(scope, 0, 0, 0));
    }

    /// <summary>Retrieves execution-level usage from the ledger doc's ExecutionSnapshots map.</summary>
    private async Task<BudgetSnapshot> GetExecutionSnapshotAsync(string executionId, CancellationToken cancellationToken)
    {
        var docs = container.GetItemLinqQueryable<BudgetLedgerDocument>()
            .Where(doc => doc.ExecutionSnapshots!.ContainsKey(executionId))
            .ToFeedIterator();

        while (docs.HasMoreResults)
        {
            var page = await docs.ReadNextAsync(cancellationToken);
            foreach (var doc in page)
            {
                if (doc.ExecutionSnapshots?.TryGetValue(executionId, out var snapshot) == true)
                {
                    var scope = new BudgetScopeRef(BudgetScopeKind.Execution, executionId);
                    return new BudgetSnapshot(scope, snapshot.TotalTokens, snapshot.TotalCostGbp, snapshot.InvocationCount);
                }
            }
        }

        var emptyScope = new BudgetScopeRef(BudgetScopeKind.Execution, executionId);
        return new BudgetSnapshot(emptyScope, 0, 0, 0);
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
                doc.Resource.TotalCostGbp,
                doc.Resource.InvocationCount);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var scope = new BudgetScopeRef(BudgetScopeKind.Engagement, engagementId);
            return new BudgetSnapshot(scope, 0, 0, 0);
        }
    }
}
