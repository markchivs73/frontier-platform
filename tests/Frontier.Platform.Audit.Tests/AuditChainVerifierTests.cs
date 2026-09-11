
namespace Frontier.Platform.Audit.Tests;

/// <summary>S5.5 tests for <see cref="AuditChainVerifier"/> (doc 05 §5, §2), extended for S13.66 key-version resolution.</summary>
public sealed class AuditChainVerifierTests
{
    private static readonly SigningKey V1 = FakeRotatingKeyProvider.V1;
    private static readonly SigningKey V2 = FakeRotatingKeyProvider.V2;

    [Fact]
    public void Verify_NoRecordForExecutionId_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AuditChainVerifier.Verify([], "eng-1::wf-1", "eng-1", Keys(V1)));

        Assert.Contains("eng-1::wf-1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_SingleRecordChainedFromGenesis_ReturnsValid()
    {
        var record = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);

        var result = AuditChainVerifier.Verify([record], record.ExecutionId, "eng-1", Keys(V1));

        Assert.True(result.SignatureValid);
        Assert.True(result.ChainValid);
        Assert.Null(result.BrokenLinkAt);
        Assert.Equal(V1.KeyId, result.VerifiedAgainstKeyId);
        Assert.Null(result.UnresolvedKeyIds);
    }

    [Fact]
    public void Verify_RecordWithOptionalFields_CarriesThemAndVerifies()
    {
        // S13.65: sandbox and the provenance stamp must survive ToSignedShape AND ToAuditRecord — an
        // asymmetric mapping re-hashes without them on verify and every new record reads as tampered.
        var unsigned = AuditRecordHasherTests.Sample() with { Sandbox = true, DynamicContextEpoch = 3, DynamicContextHash = "ctx-hash" };
        var record = Sign(unsigned, AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);

        Assert.True(record.Sandbox);
        Assert.Equal(3, record.DynamicContextEpoch);
        Assert.Equal("ctx-hash", record.DynamicContextHash);
        var result = AuditChainVerifier.Verify([record], record.ExecutionId, "eng-1", Keys(V1));
        Assert.True(result.SignatureValid);
        Assert.True(result.ChainValid);
    }

    [Fact]
    public void Verify_OptionalFieldTampered_SignatureInvalid()
    {
        // The new fields are inside the hashed bytes: editing one after signing is detectable.
        var record = Sign(AuditRecordHasherTests.Sample() with { DynamicContextEpoch = 3, DynamicContextHash = "ctx-hash" }, AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var tampered = record with { DynamicContextHash = "forged" };

        var result = AuditChainVerifier.Verify([tampered], tampered.ExecutionId, "eng-1", Keys(V1));

        Assert.False(result.SignatureValid);
        Assert.Equal(tampered.ExecutionId, result.BrokenLinkAt);
    }

    [Fact]
    public void Verify_TwoRecordChain_SecondLinksToFirst()
    {
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V1);

        var result = AuditChainVerifier.Verify([first, second], second.ExecutionId, "eng-1", Keys(V1));

        Assert.True(result.SignatureValid);
        Assert.True(result.ChainValid);
        Assert.Null(result.BrokenLinkAt);
    }

    [Fact]
    public void Verify_TargetSignedWithOldKey_VerifiedAgainstKeyIdIsThatRecordsKey()
    {
        // S13.66 decision (B): VerifiedAgainstKeyId is the target record's key id, not "the current key".
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V2);

        var result = AuditChainVerifier.Verify([first, second], first.ExecutionId, "eng-1", Keys(V1, V2));

        Assert.Equal(V1.KeyId, result.VerifiedAgainstKeyId);
        Assert.True(result.SignatureValid);
        Assert.True(result.ChainValid);
    }

    [Fact]
    public void Verify_TargetKeyUnresolved_FailsClosedAndReportsTheIdWhileTheChainStillWalks()
    {
        // S13.66 decisions (A) and (C): fail closed but distinguishable, and the hash walk continues.
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V2);

        var result = AuditChainVerifier.Verify([first, second], first.ExecutionId, "eng-1", Keys(V2));

        Assert.False(result.SignatureValid);
        Assert.Equal(V1.KeyId, result.VerifiedAgainstKeyId);
        Assert.Equal([V1.KeyId], result.UnresolvedKeyIds);
        Assert.True(result.ChainValid);
        Assert.Null(result.BrokenLinkAt);
    }

    [Fact]
    public void IsSignatureValid_UntamperedRecord_ReturnsTrue()
    {
        var record = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);

        Assert.True(AuditChainVerifier.IsSignatureValid(record, Keys(V1)));
    }

    [Fact]
    public void IsSignatureValid_RecordSignedWithOldKey_LooksUpItsOwnSigningKeyId()
    {
        // The defect: verifying a v1 record against the v2 key. With the map, its own key id wins.
        var record = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);

        Assert.True(AuditChainVerifier.IsSignatureValid(record, Keys(V1, V2)));
    }

    [Fact]
    public void IsSignatureValid_KeyIdNotInMap_ReturnsFalse()
    {
        var record = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);

        Assert.False(AuditChainVerifier.IsSignatureValid(record, Keys(V2)));
    }

    [Fact]
    public void IsSignatureValid_TamperedRecordHash_ReturnsFalse()
    {
        var record = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var tampered = record with { RecordHash = "0000000000000000000000000000000000000000000000000000000000000" };

        Assert.False(AuditChainVerifier.IsSignatureValid(tampered, Keys(V1)));
    }

    [Fact]
    public void IsSignatureValid_TamperedSignature_ReturnsFalse()
    {
        var record = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var tampered = record with { Signature = "0000000000000000000000000000000000000000000000000000000000000" };

        Assert.False(AuditChainVerifier.IsSignatureValid(tampered, Keys(V1)));
    }

    [Fact]
    public void FindBrokenLink_UnbrokenChain_ReturnsNull()
    {
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V1);

        Assert.Null(AuditChainVerifier.FindBrokenLink([first, second], "eng-1", Keys(V1)));
    }

    [Fact]
    public void FindBrokenLink_MixedKeyChain_ReturnsNull()
    {
        // The regression the defect caused: a pre-rotation record must not read as the first broken link.
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V2);

        Assert.Null(AuditChainVerifier.FindBrokenLink([first, second], "eng-1", Keys(V1, V2)));
    }

    [Fact]
    public void FindBrokenLink_MixedKeyChainTamperedAtTheV2Record_ReturnsThatExecutionId()
    {
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V2);
        var tampered = second with { Signature = "0000000000000000000000000000000000000000000000000000000000000" };

        Assert.Equal(second.ExecutionId, AuditChainVerifier.FindBrokenLink([first, tampered], "eng-1", Keys(V1, V2)));
    }

    [Fact]
    public void FindBrokenLink_RecordWhoseKeyIsMissing_IsNotABreak()
    {
        // S13.66 decision (C): hash-chain continuity needs no key, so an unresolvable key does not
        // break the chain — it is reported through SignatureValid/UnresolvedKeyIds instead.
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V2);

        Assert.Null(AuditChainVerifier.FindBrokenLink([first, second], "eng-1", Keys(V2)));
    }

    [Fact]
    public void FindBrokenLink_MissingKeyRecordFollowedByAHashBreak_ReturnsTheHashBreak()
    {
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, "not-the-first-records-hash", V2);

        Assert.Equal(second.ExecutionId, AuditChainVerifier.FindBrokenLink([first, second], "eng-1", Keys(V2)));
    }

    [Fact]
    public void FindBrokenLink_FirstRecordNotChainedFromGenesis_ReturnsItsExecutionId()
    {
        var record = Sign(AuditRecordHasherTests.Sample(), "not-the-genesis-hash", V1);

        Assert.Equal(record.ExecutionId, AuditChainVerifier.FindBrokenLink([record], "eng-1", Keys(V1)));
    }

    [Fact]
    public void FindBrokenLink_SecondRecordPreviousHashMismatch_ReturnsSecondExecutionId()
    {
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, "not-the-first-records-hash", V1);

        Assert.Equal(second.ExecutionId, AuditChainVerifier.FindBrokenLink([first, second], "eng-1", Keys(V1)));
    }

    /// <summary>The key map the verifier now takes, indexed as <c>SignedAuditRecord.SigningKeyId</c> is.</summary>
    internal static IReadOnlyDictionary<string, SigningKey> Keys(params SigningKey[] keys) =>
        keys.ToDictionary(key => key.KeyId, StringComparer.Ordinal);

    /// <summary>Signs <paramref name="record"/> as <see cref="AuditSigner.SignAsync"/> would, for chain fixtures.</summary>
    internal static SignedAuditRecord Sign(AuditRecord record, string previousRecordHash, SigningKey key)
    {
        var recordHash = AuditRecordHasher.ComputeRecordHash(record, previousRecordHash);
        var signature = AuditRecordHasher.ComputeSignature(recordHash, key.KeyMaterial);
        return AuditRecordHasher.ToSignedShape(record, previousRecordHash, recordHash, signature, key.KeyId);
    }
}
