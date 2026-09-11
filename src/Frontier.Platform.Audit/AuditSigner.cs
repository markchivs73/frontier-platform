namespace Frontier.Platform.Audit;

/// <summary>
/// <see cref="IAuditSigner"/> implementing doc 05 §5: chains an <see cref="AuditRecord"/> onto
/// its engagement's <c>audit-records</c> hash chain, signs it with the current
/// <see cref="IKeyProvider"/> key, persists it append-only, and re-verifies a stored chain on
/// demand. Signing uses the current key version; verification resolves each record's own key
/// version from its <see cref="SignedAuditRecord.SigningKeyId"/> via <see cref="SigningKeyResolver"/>,
/// so records signed before a rotation keep verifying forever (doc 05 §5, ADR-PA22).
/// </summary>
internal sealed class AuditSigner(IAuditRecordStore recordStore, IKeyProvider keyProvider) : IAuditSigner
{
    /// <inheritdoc />
    public async Task<SignedAuditRecord> SignAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var chain = await recordStore.GetChainAsync(record.EngagementId, cancellationToken);
        var previousRecordHash = chain.Count > 0
            ? chain[^1].RecordHash
            : AuditRecordHasher.ComputeGenesisHash(record.EngagementId);

        var recordHash = AuditRecordHasher.ComputeRecordHash(record, previousRecordHash);
        var key = await keyProvider.GetCurrentKeyAsync(cancellationToken);
        var signature = AuditRecordHasher.ComputeSignature(recordHash, key.KeyMaterial);
        var signed = AuditRecordHasher.ToSignedShape(record, previousRecordHash, recordHash, signature, key.KeyId);

        await recordStore.CreateAsync(signed, cancellationToken);
        return signed;
    }

    /// <inheritdoc />
    public async Task<VerificationResult> VerifyAsync(string executionId, string engagementId, CancellationToken cancellationToken)
    {
        var chain = await recordStore.GetChainAsync(engagementId, cancellationToken);
        var keys = await new SigningKeyResolver(keyProvider).ResolveAsync(chain, cancellationToken);

        return AuditChainVerifier.Verify(chain, executionId, engagementId, keys);
    }
}
