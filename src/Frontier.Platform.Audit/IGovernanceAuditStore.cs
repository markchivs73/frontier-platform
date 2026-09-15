namespace Frontier.Platform.Audit;

/// <summary>
/// Storage for the governance chain (ADR-PA30), kept behind a port so the append and retry logic is
/// unit-tested without the Cosmos SDK. <see cref="CosmosGovernanceAuditStore"/> is the adapter.
/// </summary>
internal interface IGovernanceAuditStore
{
    /// <summary>The scope's chain head and its concurrency token, or <see langword="null"/> before the first record.</summary>
    Task<GovernanceAuditHeadState?> ReadHeadAsync(string scope, CancellationToken cancellationToken);

    /// <summary>The record with <paramref name="recordId"/> in <paramref name="scope"/>, or <see langword="null"/>.</summary>
    Task<SignedGovernanceAuditRecord?> FindInScopeAsync(string scope, string recordId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically creates <paramref name="record"/>, its record-id marker, and the new head, on the condition
    /// that the head still carries <paramref name="expectedHeadETag"/> (or does not exist, when it is
    /// <see langword="null"/>). Returns <see langword="false"/> on a concurrency conflict.
    /// </summary>
    Task<bool> TryAppendAsync(SignedGovernanceAuditRecord record, string? expectedHeadETag, CancellationToken cancellationToken);

    /// <summary>The record with <paramref name="recordId"/> in any scope, or <see langword="null"/>.</summary>
    Task<SignedGovernanceAuditRecord?> GetAsync(string recordId, CancellationToken cancellationToken);

    /// <summary>Every record in <paramref name="scope"/>, in stored (document id) order.</summary>
    Task<IReadOnlyList<SignedGovernanceAuditRecord>> GetChainAsync(string scope, CancellationToken cancellationToken);

    /// <summary>One page of records matching <paramref name="query"/>.</summary>
    Task<GovernanceAuditPage> QueryAsync(GovernanceAuditQuery query, CancellationToken cancellationToken);
}

/// <summary>A chain head as read, with the concurrency token the next append must match.</summary>
internal sealed record GovernanceAuditHeadState(GovernanceAuditChainHead Head, string ETag);
