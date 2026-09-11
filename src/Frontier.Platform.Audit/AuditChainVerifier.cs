
namespace Frontier.Platform.Audit;

/// <summary>
/// Re-verification logic for a <c>audit-records</c> hash chain (doc 05 §5, §2): recomputes
/// each <see cref="SignedAuditRecord"/>'s hash/signature and confirms each record's
/// <see cref="SignedAuditRecord.PreviousRecordHash"/> matches its predecessor's
/// <see cref="SignedAuditRecord.RecordHash"/> back to genesis.
/// </summary>
internal static class AuditChainVerifier
{
    /// <summary>
    /// Builds the <see cref="VerificationResult"/> for <paramref name="executionId"/>'s record
    /// within <paramref name="chain"/> (doc 05 §2 <c>IAuditSigner.VerifyAsync</c>).
    /// </summary>
    internal static VerificationResult Verify(IReadOnlyList<SignedAuditRecord> chain, string executionId, string engagementId, IReadOnlyDictionary<string, SigningKey> keys)
    {
        var target = chain.FirstOrDefault(record => record.ExecutionId == executionId)
            ?? throw new InvalidOperationException($"No audit record found for '{executionId}' (doc 05 §6 expects one '{{executionId}}:audit' document per execution).");

        var brokenLinkAt = FindBrokenLink(chain, engagementId, keys);
        return new VerificationResult
        {
            SignatureValid = IsSignatureValid(target, keys),
            ChainValid = brokenLinkAt is null,
            BrokenLinkAt = brokenLinkAt,
            VerifiedAgainstKeyId = target.SigningKeyId,
            UnresolvedKeyIds = FindUnresolvedKeyIds(chain, keys),
        };
    }

    /// <summary>
    /// Whether <paramref name="record"/>'s <c>RecordHash</c> and <c>Signature</c> match a
    /// recomputation against the key version <paramref name="record"/> records in its
    /// <see cref="SignedAuditRecord.SigningKeyId"/> — never against "the current key" (doc 05 §5).
    /// An id absent from <paramref name="keys"/> fails closed.
    /// </summary>
    internal static bool IsSignatureValid(SignedAuditRecord record, IReadOnlyDictionary<string, SigningKey> keys)
    {
        if (!keys.TryGetValue(record.SigningKeyId, out var key))
        {
            return false;
        }

        var auditRecord = AuditRecordHasher.ToAuditRecord(record);
        var expectedRecordHash = AuditRecordHasher.ComputeRecordHash(auditRecord, record.PreviousRecordHash);
        var expectedSignature = AuditRecordHasher.ComputeSignature(expectedRecordHash, key.KeyMaterial);

        return expectedRecordHash == record.RecordHash && expectedSignature == record.Signature;
    }

    /// <summary>
    /// The distinct <see cref="SignedAuditRecord.SigningKeyId"/> values in <paramref name="chain"/>
    /// that <paramref name="keys"/> cannot resolve, in chain order, or <see langword="null"/> when
    /// every id resolved. Reported chain-wide, not just for the verified record: a caller auditing
    /// a chain needs to know a key version is gone even when its own record verifies.
    /// </summary>
    internal static IReadOnlyList<string>? FindUnresolvedKeyIds(IReadOnlyList<SignedAuditRecord> chain, IReadOnlyDictionary<string, SigningKey> keys)
    {
        string[] unresolved =
        [
            .. chain.Select(record => record.SigningKeyId)
                .Distinct(StringComparer.Ordinal)
                .Where(keyId => !keys.ContainsKey(keyId)),
        ];

        return unresolved.Length > 0 ? unresolved : null;
    }

    /// <summary>
    /// Walks <paramref name="chain"/> from genesis, returning the <see cref="SignedAuditRecord.ExecutionId"/>
    /// of the first record whose <see cref="SignedAuditRecord.PreviousRecordHash"/> doesn't match its
    /// predecessor's <see cref="SignedAuditRecord.RecordHash"/> or whose signature fails recomputation,
    /// or <see langword="null"/> if the chain is unbroken back to genesis.
    /// </summary>
    internal static string? FindBrokenLink(IReadOnlyList<SignedAuditRecord> chain, string engagementId, IReadOnlyDictionary<string, SigningKey> keys)
    {
        var expectedPreviousHash = AuditRecordHasher.ComputeGenesisHash(engagementId);

        foreach (var record in chain)
        {
            if (IsChainBreak(record, expectedPreviousHash, keys))
            {
                return record.ExecutionId;
            }

            expectedPreviousHash = record.RecordHash;
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="record"/> breaks the chain at <paramref name="expectedPreviousHash"/>:
    /// a hash discontinuity, or a signature that fails against a key version that <em>is</em>
    /// available. An unresolvable key version is not a break — hash continuity needs no key, so the
    /// walk continues and the missing version is reported through
    /// <see cref="VerificationResult.UnresolvedKeyIds"/> instead (ADR-PA22).
    /// </summary>
    internal static bool IsChainBreak(SignedAuditRecord record, string expectedPreviousHash, IReadOnlyDictionary<string, SigningKey> keys) =>
        record.PreviousRecordHash != expectedPreviousHash
            || (keys.ContainsKey(record.SigningKeyId) && !IsSignatureValid(record, keys));
}
