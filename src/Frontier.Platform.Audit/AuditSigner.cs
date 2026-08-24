using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// <see cref="IAuditSigner"/> implementing doc 05 §5: chains an <see cref="AuditRecord"/> onto
/// its engagement's <c>audit-records</c> hash chain, signs it with the current
/// <see cref="IKeyProvider"/> key, persists it append-only, and re-verifies a stored chain on
/// demand.
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
    public async Task<VerificationResult> VerifyAsync(string executionId, CancellationToken cancellationToken)
    {
        var (engagementId, _) = ExecutionId.Parse(executionId);
        var chain = await recordStore.GetChainAsync(engagementId, cancellationToken);
        var key = await keyProvider.GetCurrentKeyAsync(cancellationToken);

        return AuditChainVerifier.Verify(chain, executionId, engagementId, key);
    }
}
