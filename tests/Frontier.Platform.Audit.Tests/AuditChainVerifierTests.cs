using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Serialization;


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

    /// <summary>ADR-PA41: MCP steps sit inside the hashed bytes, so a record naming them signs and verifies, and an edited contract hash is caught.</summary>
    [Fact]
    public void Verify_RecordWithMcpToolInvocations_VerifiesAndAnEditedContractHashIsCaught()
    {
        var unsigned = AuditRecordHasherTests.Sample() with { McpToolInvocations = AuditContractSamples.AuditRecordWithMcpToolInvocations().McpToolInvocations };
        var record = Sign(unsigned, AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var tampered = record with { McpToolInvocations = [record.McpToolInvocations![0] with { InputContractHash = "sha256:forged" }] };

        var valid = AuditChainVerifier.Verify([record], record.ExecutionId, "eng-1", Keys(V1));
        var broken = AuditChainVerifier.Verify([tampered], tampered.ExecutionId, "eng-1", Keys(V1));

        Assert.True(valid.SignatureValid);
        Assert.True(valid.ChainValid);
        Assert.False(broken.SignatureValid);
        Assert.Equal(tampered.ExecutionId, broken.BrokenLinkAt);
    }

    /// <summary>S13.183 (ADR-PA45): the worker build (agent and MCP) and the generation sit inside the hashed bytes, so editing any of them is caught.</summary>
    [Fact]
    public void Verify_RecordWithHostBuildAndGeneration_VerifiesAndAnEditOfAnyIsCaught()
    {
        var record = SignedWithHostBuildAndGeneration();
        var forgedAgentBuild = record with { AgentInvocations = [record.AgentInvocations[0] with { HostBuild = "0.0.0+forged" }] };
        var forgedMcpBuild = record with { McpToolInvocations = [record.McpToolInvocations![0] with { HostBuild = "0.0.0+forged" }] };
        var forgedGeneration = record with { Generation = 2 };

        Assert.True(AuditChainVerifier.Verify([record], record.ExecutionId, "eng-1", Keys(V1)).SignatureValid);
        Assert.False(AuditChainVerifier.Verify([forgedAgentBuild], forgedAgentBuild.ExecutionId, "eng-1", Keys(V1)).SignatureValid);
        Assert.False(AuditChainVerifier.Verify([forgedMcpBuild], forgedMcpBuild.ExecutionId, "eng-1", Keys(V1)).SignatureValid);
        Assert.False(AuditChainVerifier.Verify([forgedGeneration], forgedGeneration.ExecutionId, "eng-1", Keys(V1)).SignatureValid);
    }

    /// <summary>
    /// S13.183 (ADR-PA45): the canonical profile skips a member its contract does not declare, so a reader whose
    /// contract predates the three members reads a new record without them. Stripping them from the stored bytes
    /// reproduces that read, and the recomputed signature fails: verifiers must not lag the workers that write them.
    /// </summary>
    [Fact]
    public void Verify_RecordReadWithoutTheNewMembers_FailsTheSignature()
    {
        var record = SignedWithHostBuildAndGeneration();
        var stored = JsonNode.Parse(CanonicalProfile.SerializeCanonical(record))!.AsObject();
        stored["unknown_member"] = "skipped";
        StripHostBuildAndGeneration(stored);

        var asOlderReaderSeesIt = stored.Deserialize<SignedAuditRecord>(CanonicalProfile.Options)!;

        Assert.Null(asOlderReaderSeesIt.Generation);
        Assert.False(AuditChainVerifier.Verify([asOlderReaderSeesIt], record.ExecutionId, "eng-1", Keys(V1)).SignatureValid);
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
    public void Verify_TargetKeyUnresolved_FailsClosedReportsTheIdAndIsUnverified()
    {
        // S13.66 decision (A): fail closed but distinguishable, and the hash walk continues. Decision (C) is revised
        // by S13.334 (Mark, 2026-11-24): the chain is unverified, never valid.
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V2);

        var result = AuditChainVerifier.Verify([first, second], first.ExecutionId, "eng-1", Keys(V2));

        Assert.False(result.SignatureValid);
        Assert.Equal(V1.KeyId, result.VerifiedAgainstKeyId);
        Assert.Equal([V1.KeyId], result.UnresolvedKeyIds);
        Assert.Equal(AuditChainVerdict.Unverified, result.Verdict);
        Assert.False(result.ChainValid);
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

        Assert.Null(BrokenLinkAt([first, second], Keys(V1)));
    }

    [Fact]
    public void FindBrokenLink_MixedKeyChain_ReturnsNull()
    {
        // The regression the defect caused: a pre-rotation record must not read as the first broken link.
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V2);

        Assert.Null(BrokenLinkAt([first, second], Keys(V1, V2)));
    }

    [Fact]
    public void FindBrokenLink_MixedKeyChainTamperedAtTheV2Record_ReturnsThatExecutionId()
    {
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V2);
        var tampered = second with { Signature = "0000000000000000000000000000000000000000000000000000000000000" };

        Assert.Equal(second.ExecutionId, BrokenLinkAt([first, tampered], Keys(V1, V2)));
    }

    [Fact]
    public void FindBrokenLink_RecordWhoseKeyIsMissing_IsNotABreak()
    {
        // S13.66 decision (C), still true of the link: hash-chain continuity needs no key, so an unresolvable key
        // is no broken link. Since S13.334 it makes the verdict unverified instead.
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, first.RecordHash, V2);

        Assert.Null(BrokenLinkAt([first, second], Keys(V2)));
    }

    [Fact]
    public void FindBrokenLink_MissingKeyRecordFollowedByAHashBreak_ReturnsTheHashBreak()
    {
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, "not-the-first-records-hash", V2);

        Assert.Equal(second.ExecutionId, BrokenLinkAt([first, second], Keys(V2)));
    }

    [Fact]
    public void FindBrokenLink_FirstRecordNotChainedFromGenesis_ReturnsItsExecutionId()
    {
        var record = Sign(AuditRecordHasherTests.Sample(), "not-the-genesis-hash", V1);

        Assert.Equal(record.ExecutionId, BrokenLinkAt([record], Keys(V1)));
    }

    [Fact]
    public void FindBrokenLink_SecondRecordPreviousHashMismatch_ReturnsSecondExecutionId()
    {
        var first = Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
        var second = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2" }, "not-the-first-records-hash", V1);

        Assert.Equal(second.ExecutionId, BrokenLinkAt([first, second], Keys(V1)));
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
    private static SignedAuditRecord SignedWithHostBuildAndGeneration()
    {
        var sample = AuditContractSamples.AuditRecordWithHostBuildAndGeneration();
        var unsigned = AuditRecordHasherTests.Sample() with { AgentInvocations = sample.AgentInvocations, McpToolInvocations = sample.McpToolInvocations, Generation = sample.Generation };
        return Sign(unsigned, AuditRecordHasher.ComputeGenesisHash("eng-1"), V1);
    }

    private static void StripHostBuildAndGeneration(JsonObject stored)
    {
        stored.Remove("generation");
        foreach (var invocation in stored["agent_invocations"]!.AsArray().Concat(stored["mcp_tool_invocations"]!.AsArray()))
        {
            invocation!.AsObject().Remove("host_build");
        }
    }

    // ── S13.334: the three-way verdict (B1), the keyless hash check (1(a)) and the head check (B2) ──

    /// <summary>Three linked records signed with <paramref name="key"/>, as the platform appends them.</summary>
    private static SignedAuditRecord[] Linear(SigningKey key, int count = 3)
    {
        var records = new List<SignedAuditRecord>();
        var previous = AuditRecordHasher.ComputeGenesisHash("eng-1");
        for (var i = 1; i <= count; i++)
        {
            var record = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = $"eng-1::wf-{i}", WorkflowId = $"wf-{i}" }, previous, key);
            records.Add(record);
            previous = record.RecordHash;
        }

        return [.. records];
    }

    [Fact]
    public void Verify_WholeChainWithItsHead_IsValid()
    {
        var chain = Linear(V1);

        var result = AuditChainVerifier.Verify(chain, chain[0].ExecutionId, "eng-1", Keys(V1), Head(3, chain[2], 0));

        Assert.Equal(AuditChainVerdict.Valid, result.Verdict);
        Assert.True(result.ChainValid);
        Assert.Null(result.HeadMismatch);
    }

    /// <summary>AUD-1: a full rewrite under bogus key ids (hashes recomputed and relinked) never verifies as valid.</summary>
    [Fact]
    public void Verify_ChainRewrittenUnderBogusKeyIds_IsUnverifiedNeverValid()
    {
        var bogus = V1 with { KeyId = "bogus/v9" };
        var chain = Linear(bogus);

        var result = AuditChainVerifier.Verify(chain, chain[2].ExecutionId, "eng-1", Keys(V1), Head(3, chain[2], 0));

        Assert.Equal(AuditChainVerdict.Unverified, result.Verdict);
        Assert.False(result.ChainValid);
        Assert.Equal(["bogus/v9"], result.UnresolvedKeyIds);
    }

    /// <summary>Item 1(a): content edited and the stored hash left alone is broken even when the record's key cannot be resolved.</summary>
    [Fact]
    public void Verify_ContentEditedHashLeft_UnderAnUnresolvableKey_IsBrokenAtThatRecord()
    {
        var chain = Linear(V2);
        chain[1] = chain[1] with { WorkflowId = "wf-edited" };

        var result = AuditChainVerifier.Verify(chain, chain[0].ExecutionId, "eng-1", Keys(V1));

        Assert.Equal(AuditChainVerdict.Broken, result.Verdict);
        Assert.Equal(chain[1].ExecutionId, result.BrokenLinkAt);
        Assert.Equal([V2.KeyId], result.UnresolvedKeyIds);
    }

    /// <summary>AUD-2: deleting the newest record leaves an intact shorter chain; the head names what is missing.</summary>
    [Fact]
    public void Verify_TruncatedTail_IsBrokenWithAHeadMismatch()
    {
        var chain = Linear(V1);

        var result = AuditChainVerifier.Verify(chain[..2], chain[0].ExecutionId, "eng-1", Keys(V1), Head(3, chain[2], 0));

        Assert.Equal(AuditChainVerdict.Broken, result.Verdict);
        Assert.False(result.ChainValid);
        Assert.Equal(new AuditChainHeadMismatch { HeadSequence = 3, HeadRecordHash = chain[2].RecordHash, WalkedCount = 2, WalkedRecordHash = chain[1].RecordHash }, result.HeadMismatch);
    }

    [Fact]
    public void Verify_HeadNamesAnotherHashAtItsPosition_IsBroken()
    {
        var chain = Linear(V1);

        var result = AuditChainVerifier.Verify(chain, chain[0].ExecutionId, "eng-1", Keys(V1), Head(3, chain[1], 0));

        Assert.Equal(AuditChainVerdict.Broken, result.Verdict);
        Assert.Equal(chain[2].RecordHash, result.HeadMismatch!.WalkedRecordHash);
    }

    /// <summary>The head is read before the chain, so a record appended in between makes the walk longer than the head: still valid.</summary>
    [Fact]
    public void Verify_RecordAppendedAfterTheHeadWasRead_IsStillValid()
    {
        var chain = Linear(V1);

        var result = AuditChainVerifier.Verify(chain, chain[0].ExecutionId, "eng-1", Keys(V1), Head(2, chain[1], 0));

        Assert.Equal(AuditChainVerdict.Valid, result.Verdict);
        Assert.Null(result.HeadMismatch);
    }

    /// <summary>A legacy fork with a later guarded append on the untaken arm: the fork explains the shape, so no head mismatch is reported.</summary>
    [Fact]
    public void Verify_LegacyForkWithALaterAppendOnTheUntakenArm_ReportsTheForkNotAHeadMismatch()
    {
        var (first, second) = Fork();
        var third = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-3", WorkflowId = "wf-3" }, second.RecordHash, V1);

        var result = AuditChainVerifier.Verify([first, second, third], first.ExecutionId, "eng-1", Keys(V1), Head(3, third, 2));

        Assert.Null(result.HeadMismatch);
        Assert.NotNull(result.Forks);
        Assert.Equal(AuditChainVerdict.Broken, result.Verdict);
    }

    /// <summary>An unreachable record already explains the shape, so the head is not checked even when it disagrees.</summary>
    [Fact]
    public void Verify_HeadPresentAndAnUnreachableRecord_ReportsNoHeadMismatch()
    {
        var chain = Linear(V1);
        var orphan = Sign(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-orphan", WorkflowId = "wf-orphan" }, "not-on-the-chain", V1);

        var result = AuditChainVerifier.Verify([.. chain, orphan], chain[0].ExecutionId, "eng-1", Keys(V1), Head(4, orphan, 0));

        Assert.Equal(["eng-1::wf-orphan"], result.UnreachableRecords);
        Assert.Null(result.HeadMismatch);
        Assert.Equal(AuditChainVerdict.Broken, result.Verdict);
    }

    [Fact]
    public void Verify_NoHead_ChecksNoHead() =>
        Assert.Null(AuditChainVerifier.Verify(Linear(V1)[..2], "eng-1::wf-1", "eng-1", Keys(V1), head: null).HeadMismatch);

    [Fact]
    public void InspectHead_NothingWalkedOrANonsenseSequence_IsAMismatch()
    {
        var chain = Linear(V1);

        Assert.Null(AuditChainVerifier.InspectHead([], null));
        Assert.Null(AuditChainVerifier.InspectHead([], Head(1, chain[0], 0))!.WalkedRecordHash);
        Assert.Equal(0, AuditChainVerifier.InspectHead(chain, Head(0, chain[0], 0))!.HeadSequence);
    }

    /// <summary>Damage outranks a missing key: the verdict is broken and the unresolved ids are still listed.</summary>
    [Fact]
    public void Verify_BrokenAndUnresolved_IsBrokenWithTheIdsListed()
    {
        var chain = Linear(V2);
        chain[2] = chain[2] with { WorkflowId = "wf-edited" };

        var result = AuditChainVerifier.Verify(chain, chain[0].ExecutionId, "eng-1", Keys(V1));

        Assert.Equal(AuditChainVerdict.Broken, result.Verdict);
        Assert.Equal([V2.KeyId], result.UnresolvedKeyIds);
    }

    [Theory]
    [InlineData(false, false, "valid")]
    [InlineData(false, true, "unverified")]
    [InlineData(true, false, "broken")]
    [InlineData(true, true, "broken")]
    public void VerdictOf_DamageThenUnresolvedKeys(bool damaged, bool unresolved, string expected) =>
        Assert.Equal(expected, AuditChainVerifier.VerdictOf(damaged, unresolved ? ["k"] : null).Name);

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

    /// <summary>Where <see cref="AuditChainVerifier.Verify"/> says the chain broke, verifying its first record.</summary>
    private static string? BrokenLinkAt(IReadOnlyList<SignedAuditRecord> chain, IReadOnlyDictionary<string, SigningKey> keys) =>
        AuditChainVerifier.Verify(chain, chain[0].ExecutionId, "eng-1", keys).BrokenLinkAt;

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
