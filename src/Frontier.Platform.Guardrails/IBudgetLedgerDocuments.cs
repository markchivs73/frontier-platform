namespace Frontier.Platform.Guardrails;

/// <summary>
/// The storage half of <see cref="CosmosBudgetLedger"/> (doc 07 §6): reads and writes of the
/// <c>guardrail-ledger</c> documents and nothing else, so the ledger's retry loop and snapshot
/// selection stay unit-testable (S13.193). Writes that lose an optimistic-concurrency race throw the
/// SDK's 412/409 exception unchanged; <see cref="BudgetLedgerWriteConflict"/> classifies it.
/// </summary>
internal interface IBudgetLedgerDocuments
{
    /// <summary>Reads an engagement's ledger with its ETag; <c>(null, null)</c> when it has none.</summary>
    Task<(BudgetLedgerDocument? Document, string? ETag)> ReadAsync(string engagementId, CancellationToken cancellationToken);

    /// <summary>Creates the engagement's first ledger document; a concurrent first create surfaces as a 409.</summary>
    Task CreateAsync(BudgetLedgerDocument document, CancellationToken cancellationToken);

    /// <summary>Replaces the ledger document only while it is still at <paramref name="etag"/>; a stale ETag surfaces as a 412.</summary>
    Task ReplaceAsync(BudgetLedgerDocument document, string? etag, CancellationToken cancellationToken);

    /// <summary>The ledger documents that hold a snapshot for <paramref name="executionId"/>, across engagements.</summary>
    IAsyncEnumerable<BudgetLedgerDocument> FindByExecutionAsync(string executionId, CancellationToken cancellationToken);
}
