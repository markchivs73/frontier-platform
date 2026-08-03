using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit.Tests;

/// <summary>S5.5 tests for <see cref="AuditSigner"/> (doc 05 §5, §2).</summary>
public sealed class AuditSignerTests
{
    private static readonly DevKeyProvider KeyProvider = new();

    [Fact]
    public async Task SignAsync_NullRecord_ThrowsArgumentNullException()
    {
        var signer = new AuditSigner(new FakeAuditRecordStore(), KeyProvider);

        await Assert.ThrowsAsync<ArgumentNullException>(() => signer.SignAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task SignAsync_NoPriorChain_ChainsFromGenesisHash()
    {
        var signer = new AuditSigner(new FakeAuditRecordStore(), KeyProvider);
        var record = AuditRecordHasherTests.Sample();

        var signed = await signer.SignAsync(record, CancellationToken.None);

        Assert.Equal(AuditRecordHasher.ComputeGenesisHash(record.EngagementId), signed.PreviousRecordHash);
        Assert.Equal(AuditRecordHasher.ComputeRecordHash(record, signed.PreviousRecordHash), signed.RecordHash);
    }

    [Fact]
    public async Task SignAsync_SetsSignatureAndSigningKeyIdFromCurrentKey()
    {
        var signer = new AuditSigner(new FakeAuditRecordStore(), KeyProvider);
        var key = await KeyProvider.GetCurrentKeyAsync(CancellationToken.None);

        var signed = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        Assert.Equal(key.KeyId, signed.SigningKeyId);
        Assert.Equal(AuditRecordHasher.ComputeSignature(signed.RecordHash, key.KeyMaterial), signed.Signature);
    }

    [Fact]
    public async Task SignAsync_PersistsViaRecordStore()
    {
        var store = new FakeAuditRecordStore();
        var signer = new AuditSigner(store, KeyProvider);

        var signed = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        var chain = await store.GetChainAsync(signed.EngagementId, CancellationToken.None);
        Assert.Equal([signed], chain);
    }

    [Fact]
    public async Task SignAsync_SecondRecordForEngagement_ChainsFromFirstRecordsHash()
    {
        var store = new FakeAuditRecordStore();
        var signer = new AuditSigner(store, KeyProvider);
        var first = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        var second = await signer.SignAsync(
            AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2", ClosedAtUtc = first.ClosedAtUtc.AddMinutes(1) },
            CancellationToken.None);

        Assert.Equal(first.RecordHash, second.PreviousRecordHash);
    }

    [Fact]
    public async Task SignAsync_DuplicateExecution_ThrowsFromRecordStore()
    {
        var store = new FakeAuditRecordStore();
        var signer = new AuditSigner(store, KeyProvider);
        await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None));
    }

    [Fact]
    public async Task VerifyAsync_SignedRecord_ReturnsValid()
    {
        var store = new FakeAuditRecordStore();
        var signer = new AuditSigner(store, KeyProvider);
        var signed = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        var result = await signer.VerifyAsync(signed.ExecutionId, CancellationToken.None);

        Assert.True(result.SignatureValid);
        Assert.True(result.ChainValid);
        Assert.Null(result.BrokenLinkAt);
        Assert.Equal((await KeyProvider.GetCurrentKeyAsync(CancellationToken.None)).KeyId, result.VerifiedAgainstKeyId);
    }

    [Fact]
    public async Task VerifyAsync_TamperedStoredRecord_ReturnsBrokenChainAtThatExecution()
    {
        var store = new FakeAuditRecordStore();
        var signer = new AuditSigner(store, KeyProvider);
        var signed = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);
        store.Replace(signed with { FinalStatus = ExecutionStatus.Failed });

        var result = await signer.VerifyAsync(signed.ExecutionId, CancellationToken.None);

        Assert.False(result.SignatureValid);
        Assert.False(result.ChainValid);
        Assert.Equal(signed.ExecutionId, result.BrokenLinkAt);
    }

    [Fact]
    public async Task VerifyAsync_NoRecordForExecution_Throws()
    {
        var signer = new AuditSigner(new FakeAuditRecordStore(), KeyProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => signer.VerifyAsync("eng-1::wf-1", CancellationToken.None));
    }
}
