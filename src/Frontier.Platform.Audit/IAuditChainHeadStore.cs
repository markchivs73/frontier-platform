namespace Frontier.Platform.Audit;

/// <summary>
/// The conditional-append half of the <c>audit-records</c> container (ADR-PA31), kept behind an
/// internal port so the append and retry logic is unit-tested without the Cosmos SDK.
/// <see cref="CosmosAuditRecordStore"/> is the adapter, and it implements this alongside
/// <see cref="IAuditRecordStore"/>.
///
/// <para>
/// It is deliberately <em>not</em> on the public <see cref="IAuditRecordStore"/>. That interface is
/// shipped and implemented by consumers (the recovery sweeper's test doubles among them); adding
/// members to it would be a source break for every implementor, and the head is platform
/// bookkeeping no consumer needs to write.
/// </para>
/// </summary>
internal interface IAuditChainHeadStore
{
    /// <summary>
    /// The engagement's chain head and its concurrency token, or <see langword="null"/> when no head
    /// document exists — either the engagement has no records at all, or its chain predates ADR-PA31
    /// and has not yet been appended to since the upgrade.
    /// </summary>
    Task<AuditChainHeadState?> ReadHeadAsync(string engagementId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically creates <paramref name="record"/>'s <c>{executionId}:audit</c> document and writes
    /// <paramref name="head"/>, on the condition that the head still carries
    /// <paramref name="expectedHeadETag"/> (or does not exist, when it is <see langword="null"/>).
    /// Returns <see langword="false"/> on a concurrency conflict, so the caller re-reads and re-hashes.
    /// </summary>
    Task<bool> TryAppendAsync(SignedAuditRecord record, AuditChainHead head, string? expectedHeadETag, CancellationToken cancellationToken);
}

/// <summary>
/// An engagement's chain head: what the next append must chain from (ADR-PA31). It is mutable
/// bookkeeping, not evidence — it is never signed, and the signed records remain the truth.
/// </summary>
/// <param name="EngagementId">The engagement whose chain this heads.</param>
/// <param name="Sequence">How many records the chain holds, the one this head names included.</param>
/// <param name="RecordHash">The latest record's hash — the next record's <c>previous_record_hash</c>.</param>
/// <param name="LastExecutionId">The execution whose record this head names.</param>
/// <param name="UnguardedRecordCount">
/// How many records already existed when this head was first created — the records that were written
/// before the guard and so could have forked. It is fixed at head creation and never moves again.
/// </param>
internal sealed record AuditChainHead(
    string EngagementId,
    long Sequence,
    string RecordHash,
    string LastExecutionId,
    long UnguardedRecordCount);

/// <summary>A chain head as read, with the concurrency token the next append must match.</summary>
internal sealed record AuditChainHeadState(AuditChainHead Head, string ETag);
