using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Guardrails;

/// <summary><see cref="IBudgetLedgerDocuments"/> over the <c>guardrail-ledger</c> container (partition key <c>/engagement_id</c>).</summary>
[ExcludeFromCodeCoverage(Justification = "Cosmos SDK glue: one SDK call per member and a 404 translation, its decisions live in CosmosBudgetLedger; exercised by the emulator integration tests in CI's integration job, not the unit-coverage gate.")]
internal sealed class CosmosBudgetLedgerDocuments(Container container) : IBudgetLedgerDocuments
{
    private readonly Container container = container ?? throw new ArgumentNullException(nameof(container));

    /// <inheritdoc />
    public async Task<(BudgetLedgerDocument? Document, string? ETag)> ReadAsync(string engagementId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<BudgetLedgerDocument>(
                BudgetLedgerAccumulation.DocumentId(engagementId), new PartitionKey(engagementId), cancellationToken: cancellationToken);
            return (response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return (null, null);
        }
    }

    /// <inheritdoc />
    public Task CreateAsync(BudgetLedgerDocument document, CancellationToken cancellationToken) =>
        container.CreateItemAsync(document, new PartitionKey(document.EngagementId), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task ReplaceAsync(BudgetLedgerDocument document, string? etag, CancellationToken cancellationToken) =>
        container.ReplaceItemAsync(document, document.Id, new PartitionKey(document.EngagementId), new ItemRequestOptions { IfMatchEtag = etag }, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<BudgetLedgerDocument> FindByExecutionAsync(string executionId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // SQL, not LINQ: the Cosmos LINQ provider cannot translate Dictionary.ContainsKey.
        var query = new QueryDefinition("SELECT * FROM c WHERE IS_DEFINED(c.execution_snapshots[@executionId])")
            .WithParameter("@executionId", executionId);
        using var documents = container.GetItemQueryIterator<BudgetLedgerDocument>(query);
        while (documents.HasMoreResults)
        {
            foreach (var document in await documents.ReadNextAsync(cancellationToken))
            {
                yield return document;
            }
        }
    }
}
