using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// <see cref="IAuditSigner"/> implementing doc 05 §5: chains an <see cref="AuditRecord"/> onto
/// its engagement's <c>audit-records</c> hash chain, signs it with the current
/// <see cref="IKeyProvider"/> key, persists it append-only, and re-verifies a stored chain on
/// demand. Signing uses the current key version; verification resolves each record's own key
/// version from its <see cref="SignedAuditRecord.SigningKeyId"/> via <see cref="SigningKeyResolver"/>,
/// so records signed before a rotation keep verifying forever (doc 05 §5, ADR-PA22).
///
/// <para>
/// The append is guarded (ADR-PA31). K9 allows one live execution per engagement-<em>workflow</em>,
/// so two workflows on one engagement can close at the same moment; the old read-then-create let
/// both chain from the same predecessor and fork the chain. Now the append reads the engagement's
/// chain head with its ETag, signs against it, and writes the record and the new head in one
/// conditional transactional batch. Losing that race is not a failure — it re-reads, re-hashes and
/// re-signs against the winner.
/// </para>
/// </summary>
internal sealed class AuditSigner(
    IAuditRecordStore recordStore,
    IAuditChainHeadStore headStore,
    IKeyProvider keyProvider,
    IAuditSigningService signingService,
    IOptions<ExecutionAuditOptions> options) : IAuditSigner
{
    /// <inheritdoc />
    public async Task<SignedAuditRecord> SignAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var retry = options.Value;
        for (var attempt = 1; attempt <= retry.AppendMaxAttempts; attempt++)
        {
            if (await TrySignOnceAsync(record, cancellationToken) is { } signed)
            {
                return signed;
            }

            // A conflict on an execution whose record already exists is not a lost race:
            // audit-records is append-only and a closed execution's record never changes
            // (doc 05 §6). Fail fast with the same exception the create-only store has always
            // thrown, rather than burning the retry budget to reach a different one.
            if (await recordStore.GetAsync(record.ExecutionId, record.EngagementId, cancellationToken) is not null)
            {
                throw new InvalidOperationException(
                    $"An audit record for '{record.ExecutionId}' already exists (audit-records is append-only, doc 05 §6).");
            }

            if (attempt < retry.AppendMaxAttempts)
            {
                await Task.Delay(AuditAppendBackoff.DelayFor(attempt, retry, AuditAppendBackoff.NextJitter()), cancellationToken);
            }
        }

        throw new AuditChainAppendException(
            $"The audit append for execution '{record.ExecutionId}' kept losing engagement '{record.EngagementId}'s chain head to concurrent closes; the configured attempts are exhausted. The record is NOT stored.");
    }

    /// <inheritdoc />
    public async Task<VerificationResult> VerifyAsync(string executionId, string engagementId, CancellationToken cancellationToken)
    {
        var chain = await recordStore.GetChainAsync(engagementId, cancellationToken);
        var head = await headStore.ReadHeadAsync(engagementId, cancellationToken);
        var keys = await new SigningKeyResolver(keyProvider).ResolveAsync(chain, cancellationToken);

        return AuditChainVerifier.Verify(chain, executionId, engagementId, keys, head?.Head);
    }

    /// <summary>
    /// One read-hash-sign-store pass: the stored record, or <see langword="null"/> when another
    /// close moved the head first.
    /// </summary>
    internal async Task<SignedAuditRecord?> TrySignOnceAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        var anchor = await ReadAnchorAsync(record.EngagementId, cancellationToken);

        var recordHash = AuditRecordHasher.ComputeRecordHash(record, anchor.PreviousRecordHash);
        var signature = await signingService.SignAsync(recordHash, cancellationToken);
        var signed = AuditRecordHasher.ToSignedShape(record, anchor.PreviousRecordHash, recordHash, signature.Signature, signature.KeyId);

        var head = new AuditChainHead(
            record.EngagementId,
            anchor.Sequence + 1,
            recordHash,
            record.ExecutionId,
            anchor.UnguardedRecordCount);

        return await headStore.TryAppendAsync(signed, head, anchor.ETag, cancellationToken) ? signed : null;
    }

    /// <summary>
    /// What the next record chains from. The head is authoritative when it exists. When it does not,
    /// the engagement's chain either is empty (genesis) or predates ADR-PA31, and the anchor is
    /// recovered from the stored chain's tail — the first append after the upgrade creates the head
    /// from it, with no backfill job and nothing stored rewritten (ADR-PA31 migration).
    /// </summary>
    internal async Task<AuditChainAnchor> ReadAnchorAsync(string engagementId, CancellationToken cancellationToken)
    {
        if (await headStore.ReadHeadAsync(engagementId, cancellationToken) is { } state)
        {
            return new AuditChainAnchor(state.Head.RecordHash, state.Head.Sequence, state.Head.UnguardedRecordCount, state.ETag);
        }

        var chain = await recordStore.GetChainAsync(engagementId, cancellationToken);
        var previousRecordHash = chain.Count > 0
            ? chain[^1].RecordHash
            : AuditRecordHasher.ComputeGenesisHash(engagementId);

        // The whole existing chain predates the guard, so every one of its records is one that could
        // have forked; the count is frozen into the head the batch is about to create.
        return new AuditChainAnchor(previousRecordHash, chain.Count, chain.Count, ETag: null);
    }
}

/// <summary>
/// What one append pass chains from (ADR-PA31): the predecessor hash, the sequence it follows, the
/// unguarded-record count to carry forward, and the head's concurrency token —
/// <see langword="null"/> when the head does not exist yet and the batch must create it.
/// </summary>
internal sealed record AuditChainAnchor(string PreviousRecordHash, long Sequence, long UnguardedRecordCount, string? ETag);
