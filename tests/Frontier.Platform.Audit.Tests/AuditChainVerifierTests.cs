
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

    [Fact]
    public void Verify_LinearChain_ReportsNoForks()
    {
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V1);

        var result = AuditChainVerifier.Verify([first, second], first.ExecutionId, "eng-1", Keys(V1), Head(2, second, unguarded: 0));

        Assert.Null(result.Forks);
        Assert.True(result.ChainValid);
    }

    [Fact]
    public void Verify_ForkWithNoHeadAtAll_IsLegacyAndNeverASignatureMismatch()
    {
        // S13.106's own footprint: two closes read the same tail and both chained from it. Nothing
        // was altered — both records verify against their own key — so this must never read as tamper.
        var (first, second) = Fork();

        var result = AuditChainVerifier.Verify([first, second], second.ExecutionId, "eng-1", Keys(V1), head: null);

        var fork = Assert.Single(result.Forks!);
        Assert.Equal(AuditChainForkKind.Legacy, fork.Kind);
        Assert.Equal(AuditRecordHasher.ComputeGenesisHash("eng-1"), fork.PreviousRecordHash);
        Assert.Equal([first.ExecutionId, second.ExecutionId], fork.ExecutionIds);
        Assert.True(result.SignatureValid);
        Assert.Null(result.UnresolvedKeyIds);
        Assert.False(result.ChainValid);
    }

    [Fact]
    public void Verify_ForkInsideTheUnguardedPrefix_IsLegacy()
    {
        // The head exists, but it was created after these two records already sat in the chain.
        var (first, second) = Fork();

        var result = AuditChainVerifier.Verify([first, second], first.ExecutionId, "eng-1", Keys(V1), Head(2, second, unguarded: 2));

        Assert.Equal(AuditChainForkKind.Legacy, Assert.Single(result.Forks!).Kind);
        Assert.True(result.SignatureValid);
    }

    [Fact]
    public void Verify_ForkReachingBeyondTheUnguardedPrefix_IsGuarded()
    {
        // Only one record predates the head, so the second arm was written while the ETag guard was
        // in force. The platform cannot produce that — it is a genuine finding, not the old defect.
        var (first, second) = Fork();

        var result = AuditChainVerifier.Verify([first, second], first.ExecutionId, "eng-1", Keys(V1), Head(2, second, unguarded: 1));

        Assert.Equal(AuditChainForkKind.Guarded, Assert.Single(result.Forks!).Kind);
        Assert.False(result.ChainValid);
    }

    [Fact]
    public void Verify_ForkedChainWithATamperedArm_StillReportsTheSignatureFailure()
    {
        // A fork does not mask tampering: the arm whose content was edited still fails its signature.
        var (first, second) = Fork();
        var tampered = second with { WorkflowId = "forged" };

        var result = AuditChainVerifier.Verify([first, tampered], tampered.ExecutionId, "eng-1", Keys(V1), head: null);

        Assert.False(result.SignatureValid);
        Assert.Equal(AuditChainForkKind.Legacy, Assert.Single(result.Forks!).Kind);
    }

    [Fact]
    public void Verify_ChainStoredOutOfClosedAtOrder_VerifiesCleanByFollowingTheLinks()
    {
        // ADR-PA31 (Mark, 2026-09-16): the head race orders appends, not closed_at_utc. Here the
        // links say first→second but the stored order is the reverse, as concurrent closes produce.
        var first = Sign(AuditRecordHasherTests.Sample() with { ClosedAtUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) }, AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(
            AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2", ClosedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            first.RecordHash,
            V1);

        var result = AuditChainVerifier.Verify([second, first], first.ExecutionId, "eng-1", Keys(V1));

        Assert.True(result.ChainValid);
        Assert.Null(result.BrokenLinkAt);
        Assert.Null(result.UnreachableRecords);
    }

    [Fact]
    public void Verify_RecordTheLinksCannotReach_IsItsOwnFindingNotASignatureMismatch()
    {
        // The orphan is perfectly well signed; what is wrong is that nothing links to it.
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var orphan = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, "not-any-records-hash", V1);

        var result = AuditChainVerifier.Verify([first, orphan], orphan.ExecutionId, "eng-1", Keys(V1));

        Assert.Equal([orphan.ExecutionId], result.UnreachableRecords);
        Assert.True(result.SignatureValid);
        Assert.Null(result.Forks);
        Assert.False(result.ChainValid);
    }

    [Fact]
    public void Verify_GenuineLinkBreak_StillNamesItInBrokenLinkAt()
    {
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var broken = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, "not-the-first-records-hash", V1);

        var result = AuditChainVerifier.Verify([first, broken], first.ExecutionId, "eng-1", Keys(V1));

        Assert.Equal(broken.ExecutionId, result.BrokenLinkAt);
        Assert.False(result.ChainValid);
    }

    [Fact]
    public void Verify_TamperedRecordOnTheWalk_IsReportedAheadOfAnyUnreachableRecord()
    {
        // A tampered record still fails its own signature, and that is the finding that leads.
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var tampered = first with { WorkflowId = "forged" };
        var orphan = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-3", WorkflowId = "wf-3" }, "not-any-records-hash", V1);

        var result = AuditChainVerifier.Verify([tampered, orphan], tampered.ExecutionId, "eng-1", Keys(V1));

        Assert.Equal(tampered.ExecutionId, result.BrokenLinkAt);
        Assert.Equal([orphan.ExecutionId], result.UnreachableRecords);
        Assert.False(result.SignatureValid);
    }

    [Fact]
    public void Verify_ChainThatLinksBackOnItself_TerminatesInsteadOfLooping()
    {
        // A crafted cycle must not hang the verifier. No key is supplied, so the walk is pure link
        // following: genesis → a → b → genesis, where `a` has already been walked.
        var genesis = AuditRecordHasher.ComputeGenesisHash("eng-1");
        var a = AuditRecordHasher.ToSignedShape(AuditRecordHasherTests.Sample(), genesis, "HASH-A", "sig", V1.KeyId);
        var b = AuditRecordHasher.ToSignedShape(
            AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" },
            "HASH-A",
            genesis,
            "sig",
            V1.KeyId);

        var result = AuditChainVerifier.Verify([a, b], a.ExecutionId, "eng-1", Keys());

        Assert.Null(result.UnreachableRecords);
        Assert.Equal([V1.KeyId], result.UnresolvedKeyIds);
    }

    /// <summary>Two records both chained from genesis — the fork the unguarded append produced.</summary>
    private static (SignedAuditRecord First, SignedAuditRecord Second) Fork()
    {
        var genesis = AuditRecordHasher.ComputeGenesisHash("eng-1");
        var first = Sign(AuditRecordHasherTests.Sample(), genesis, V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, genesis, V1);
        return (first, second);
    }

    /// <summary>A chain head naming <paramref name="last"/>, with <paramref name="unguarded"/> records predating it.</summary>
    private static AuditChainHead Head(long sequence, SignedAuditRecord last, long unguarded) =>
        new("eng-1", sequence, last.RecordHash, last.ExecutionId, unguarded);

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
