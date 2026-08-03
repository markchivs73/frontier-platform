
namespace Frontier.Platform.Audit.Tests;

/// <summary>S5.5 tests for <see cref="AuditChainVerifier"/> (doc 05 §5, §2).</summary>
public sealed class AuditChainVerifierTests
{
    private static readonly DevKeyProvider KeyProvider = new();

    [Fact]
    public void Verify_NoRecordForExecutionId_Throws()
    {
        var key = CurrentKey();

        var ex = Assert.Throws<InvalidOperationException>(() => AuditChainVerifier.Verify([], "eng-1::wf-1", "eng-1", key));

        Assert.Contains("eng-1::wf-1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_SingleRecordChainedFromGenesis_ReturnsValid()
    {
        var key = CurrentKey();
        var record = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), key);

        var result = AuditChainVerifier.Verify([record], record.ExecutionId, "eng-1", key);

        Assert.True(result.SignatureValid);
        Assert.True(result.ChainValid);
        Assert.Null(result.BrokenLinkAt);
        Assert.Equal(key.KeyId, result.VerifiedAgainstKeyId);
    }

    [Fact]
    public void Verify_TwoRecordChain_SecondLinksToFirst()
    {
        var key = CurrentKey();
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), key);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, key);

        var result = AuditChainVerifier.Verify([first, second], second.ExecutionId, "eng-1", key);

        Assert.True(result.SignatureValid);
        Assert.True(result.ChainValid);
        Assert.Null(result.BrokenLinkAt);
    }

    [Fact]
    public void IsSignatureValid_UntamperedRecord_ReturnsTrue()
    {
        var key = CurrentKey();
        var record = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), key);

        Assert.True(AuditChainVerifier.IsSignatureValid(record, key));
    }

    [Fact]
    public void IsSignatureValid_TamperedRecordHash_ReturnsFalse()
    {
        var key = CurrentKey();
        var record = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), key);
        var tampered = record with { RecordHash = "0000000000000000000000000000000000000000000000000000000000000" };

        Assert.False(AuditChainVerifier.IsSignatureValid(tampered, key));
    }

    [Fact]
    public void IsSignatureValid_TamperedSignature_ReturnsFalse()
    {
        var key = CurrentKey();
        var record = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), key);
        var tampered = record with { Signature = "0000000000000000000000000000000000000000000000000000000000000" };

        Assert.False(AuditChainVerifier.IsSignatureValid(tampered, key));
    }

    [Fact]
    public void FindBrokenLink_UnbrokenChain_ReturnsNull()
    {
        var key = CurrentKey();
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), key);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, key);

        Assert.Null(AuditChainVerifier.FindBrokenLink([first, second], "eng-1", key));
    }

    [Fact]
    public void FindBrokenLink_FirstRecordNotChainedFromGenesis_ReturnsItsExecutionId()
    {
        var key = CurrentKey();
        var record = Sign(AuditRecordHasherTests.Sample(), "not-the-genesis-hash", key);

        Assert.Equal(record.ExecutionId, AuditChainVerifier.FindBrokenLink([record], "eng-1", key));
    }

    [Fact]
    public void FindBrokenLink_SecondRecordPreviousHashMismatch_ReturnsSecondExecutionId()
    {
        var key = CurrentKey();
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), key);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, "not-the-first-records-hash", key);

        Assert.Equal(second.ExecutionId, AuditChainVerifier.FindBrokenLink([first, second], "eng-1", key));
    }

    private static SigningKey CurrentKey() => KeyProvider.GetCurrentKeyAsync(CancellationToken.None).Result;

    /// <summary>Signs <paramref name="record"/> as <see cref="AuditSigner.SignAsync"/> would, for chain fixtures.</summary>
    private static SignedAuditRecord Sign(AuditRecord record, string previousRecordHash, SigningKey key)
    {
        var recordHash = AuditRecordHasher.ComputeRecordHash(record, previousRecordHash);
        var signature = AuditRecordHasher.ComputeSignature(recordHash, key.KeyMaterial);
        return AuditRecordHasher.ToSignedShape(record, previousRecordHash, recordHash, signature, key.KeyId);
    }
}
