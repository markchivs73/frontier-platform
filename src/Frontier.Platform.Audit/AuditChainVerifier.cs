
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
    internal static VerificationResult Verify(IReadOnlyList<SignedAuditRecord> chain, string executionId, string engagementId, SigningKey key)
    {
        var target = chain.FirstOrDefault(record => record.ExecutionId == executionId)
            ?? throw new InvalidOperationException($"No audit record found for '{executionId}' (doc 05 §6 expects one '{{executionId}}:audit' document per execution).");

        var brokenLinkAt = FindBrokenLink(chain, engagementId, key);
        return new VerificationResult
        {
            SignatureValid = IsSignatureValid(target, key),
            ChainValid = brokenLinkAt is null,
            BrokenLinkAt = brokenLinkAt,
            VerifiedAgainstKeyId = key.KeyId,
        };
    }

    /// <summary>Whether <paramref name="record"/>'s <c>RecordHash</c> and <c>Signature</c> match a recomputation against <paramref name="key"/>.</summary>
    internal static bool IsSignatureValid(SignedAuditRecord record, SigningKey key)
    {
        var auditRecord = AuditRecordHasher.ToAuditRecord(record);
        var expectedRecordHash = AuditRecordHasher.ComputeRecordHash(auditRecord, record.PreviousRecordHash);
        var expectedSignature = AuditRecordHasher.ComputeSignature(expectedRecordHash, key.KeyMaterial);

        return expectedRecordHash == record.RecordHash && expectedSignature == record.Signature;
    }

    /// <summary>
    /// Walks <paramref name="chain"/> from genesis, returning the <see cref="SignedAuditRecord.ExecutionId"/>
    /// of the first record whose <see cref="SignedAuditRecord.PreviousRecordHash"/> doesn't match its
    /// predecessor's <see cref="SignedAuditRecord.RecordHash"/> or whose signature fails recomputation,
    /// or <see langword="null"/> if the chain is unbroken back to genesis.
    /// </summary>
    internal static string? FindBrokenLink(IReadOnlyList<SignedAuditRecord> chain, string engagementId, SigningKey key)
    {
        var expectedPreviousHash = AuditRecordHasher.ComputeGenesisHash(engagementId);

        foreach (var record in chain)
        {
            if (record.PreviousRecordHash != expectedPreviousHash || !IsSignatureValid(record, key))
            {
                return record.ExecutionId;
            }

            expectedPreviousHash = record.RecordHash;
        }

        return null;
    }
}
