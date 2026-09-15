using static Frontier.Platform.Audit.Tests.GovernanceAuditSamples;

namespace Frontier.Platform.Audit.Tests;

/// <summary>S13.103 tests for <see cref="GovernanceAuditChainVerifier"/> (ADR-PA30): valid chains, rotation, and every tamper case reporting its position.</summary>
public sealed class GovernanceAuditChainVerifierTests
{
    private static readonly SigningKey V1 = FakeRotatingKeyProvider.V1;
    private static readonly SigningKey V2 = FakeRotatingKeyProvider.V2;
    private const string Scope = GovernanceAuditScopes.Deployment;

    [Fact]
    public void Verify_GenesisRecord_IsValid()
    {
        var chain = Chain(V1);

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, HeadOf(chain), Keys(V1));

        Assert.True(result.Valid);
        Assert.Null(result.Breaks);
        Assert.Null(result.UnresolvedKeyIds);
        Assert.Equal(1, result.RecordCount);
        Assert.Equal(1, result.HeadSequence);
        Assert.Equal(Genesis, chain[0].PreviousRecordHash);
        Assert.Equal(Scope, result.Scope);
    }

    [Fact]
    public void Verify_RecordAtSequenceN_IsValid()
    {
        var chain = Chain(V1, V1, V1, V1, V1);

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, HeadOf(chain), Keys(V1));

        Assert.True(result.Valid);
        Assert.Equal(5, result.RecordCount);
        Assert.Equal(chain[3].RecordHash, chain[4].PreviousRecordHash);
    }

    [Fact]
    public void Verify_V1RecordsAfterV2IsCurrent_StillVerify()
    {
        var chain = Chain(V1, V1, V2, V2);

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, HeadOf(chain), Keys(V1, V2));

        Assert.True(result.Valid);
    }

    [Fact]
    public void Verify_UnresolvedKeyVersion_FailsClosedAtItsPosition()
    {
        var chain = Chain(V1, V2);

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, HeadOf(chain), Keys(V2));

        Assert.False(result.Valid);
        var only = Assert.Single(result.Breaks!);
        AssertBreak(only, 1, GovernanceAuditBreakKind.UnresolvedKey, chain[0]);
        Assert.Equal([V1.KeyId], result.UnresolvedKeyIds);
    }

    [Fact]
    public void Verify_MutatedPayload_ReportsSignatureMismatchAtThatRecord()
    {
        var chain = Chain(V1, V1, V1);
        chain[1] = chain[1] with { Change = Change("""{"responsibilities":["everything"]}""") };

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, HeadOf(chain), Keys(V1));

        AssertBreak(Assert.Single(result.Breaks!), 2, GovernanceAuditBreakKind.SignatureMismatch, chain[1]);
    }

    [Fact]
    public void Verify_MutatedActor_ReportsSignatureMismatchAtThatRecord()
    {
        var chain = Chain(V1, V1, V1);
        chain[2] = chain[2] with { Actor = "user:someone-else" };

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, HeadOf(chain), Keys(V1));

        AssertBreak(Assert.Single(result.Breaks!), 3, GovernanceAuditBreakKind.SignatureMismatch, chain[2]);
    }

    [Fact]
    public void Verify_DeletedMiddleRecord_ReportsGapAndLinkBreakWhereItWas()
    {
        var chain = Chain(V1, V1, V1, V1);
        chain.RemoveAt(1);

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, HeadOf(chain), Keys(V1));

        Assert.Equal(2, result.Breaks!.Count);
        AssertBreak(result.Breaks[0], 2, GovernanceAuditBreakKind.SequenceGap, chain[1]);
        AssertBreak(result.Breaks[1], 2, GovernanceAuditBreakKind.HashLinkBreak, chain[1]);
    }

    [Fact]
    public void Verify_ReorderedRecords_ReportsEachDisplacedPosition()
    {
        var original = Chain(V1, V1, V1);
        List<SignedGovernanceAuditRecord> reordered = [original[0], original[2], original[1]];

        var result = GovernanceAuditChainVerifier.Verify(Scope, reordered, HeadOf(original), Keys(V1));

        Assert.Equal(
            [(2L, "sequence_gap"), (2L, "hash_link_break"), (3L, "out_of_order"), (3L, "hash_link_break"), (3L, "head_mismatch")],
            result.Breaks!.Select(item => (item.Position, item.Kind.Name)));
    }

    [Fact]
    public void Verify_DuplicatedRecord_ReportsOutOfOrder()
    {
        var chain = Chain(V1, V1);
        chain.Add(chain[1]);

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, HeadOf(chain), Keys(V1));

        Assert.Contains(result.Breaks!, item => item.Position == 3 && item.Kind == GovernanceAuditBreakKind.OutOfOrder);
    }

    [Fact]
    public void Verify_SigningKeyIdSwappedToAnotherValidVersion_ReportsSignatureMismatch()
    {
        var chain = Chain(V1, V1);
        chain[0] = chain[0] with { SigningKeyId = V2.KeyId };

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, HeadOf(chain), Keys(V1, V2));

        AssertBreak(Assert.Single(result.Breaks!), 1, GovernanceAuditBreakKind.SignatureMismatch, chain[0]);
    }

    [Fact]
    public void Verify_SigningKeyIdSwappedToUnknownVersion_FailsClosed()
    {
        var chain = Chain(V1);
        chain[0] = chain[0] with { SigningKeyId = "dev-key/v9" };

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, HeadOf(chain), Keys(V1));

        AssertBreak(Assert.Single(result.Breaks!), 1, GovernanceAuditBreakKind.UnresolvedKey, chain[0]);
        Assert.Equal(["dev-key/v9"], result.UnresolvedKeyIds);
    }

    [Fact]
    public void Verify_ForgedHead_ReportsHeadMismatchAtTheEnd()
    {
        var chain = Chain(V1, V1, V1);
        var forged = HeadOf(chain) with { RecordHash = "F0F0" };

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, forged, Keys(V1));

        var only = Assert.Single(result.Breaks!);
        Assert.Equal(3, only.Position);
        Assert.Equal(GovernanceAuditBreakKind.HeadMismatch, only.Kind);
        Assert.Equal(3, only.RecordedSequence);
        Assert.Null(only.RecordId);
    }

    [Fact]
    public void Verify_HeadClaimsARecordThatIsNotStored_ReportsHeadMismatch()
    {
        var chain = Chain(V1, V1);
        var ahead = HeadOf(chain) with { Sequence = 3 };

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, ahead, Keys(V1));

        Assert.Equal(GovernanceAuditBreakKind.HeadMismatch, Assert.Single(result.Breaks!).Kind);
    }

    [Fact]
    public void Verify_RecordsWithoutHead_ReportsHeadMismatch()
    {
        var chain = Chain(V1);

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, null, Keys(V1));

        Assert.Equal(GovernanceAuditBreakKind.HeadMismatch, Assert.Single(result.Breaks!).Kind);
        Assert.Null(result.HeadSequence);
    }

    [Fact]
    public void Verify_HeadWithoutRecords_ReportsHeadMismatchAtPositionZero()
    {
        var head = HeadOf(Chain(V1));

        var result = GovernanceAuditChainVerifier.Verify(Scope, [], head, Keys(V1));

        var only = Assert.Single(result.Breaks!);
        Assert.Equal(0, only.Position);
        Assert.False(result.Valid);
    }

    [Fact]
    public void Verify_EmptyChainWithoutHead_IsValid()
    {
        var result = GovernanceAuditChainVerifier.Verify(Scope, [], null, Keys());

        Assert.True(result.Valid);
        Assert.Equal(0, result.RecordCount);
    }

    [Fact]
    public void Verify_UnderAnotherScope_ReportsGenesisLinkAndScopeMismatch()
    {
        var chain = Chain(V1);

        var result = GovernanceAuditChainVerifier.Verify("other_scope", chain, HeadOf(chain), Keys(V1));

        Assert.Equal(
            [GovernanceAuditBreakKind.HashLinkBreak, GovernanceAuditBreakKind.ScopeMismatch],
            result.Breaks!.Select(item => item.Kind));
    }

    [Fact]
    public void Verify_CompensatingRecordInChain_IsValid()
    {
        var chain = Chain(V1);
        var compensation = Entry(reason: "role store write failed after the audit landed") with
        {
            EventType = "approver_role_update_aborted",
            CompensatesRecordId = chain[0].RecordId,
        };
        chain.Add(Seal(compensation, 2, chain[0].RecordHash, V1));

        var result = GovernanceAuditChainVerifier.Verify(Scope, chain, HeadOf(chain), Keys(V1));

        Assert.True(result.Valid);
        Assert.Equal(chain[0].RecordId, chain[1].CompensatesRecordId);
    }

    [Theory]
    [InlineData(2, 2, null)]
    [InlineData(1, 2, "out_of_order")]
    [InlineData(3, 2, "sequence_gap")]
    public void SequenceBreak_ComparesAgainstTheWalk(long actual, long expected, string? kind) =>
        Assert.Equal(kind, GovernanceAuditChainVerifier.SequenceBreak(actual, expected)?.Name);

    [Fact]
    public void Verify_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => GovernanceAuditChainVerifier.Verify(null!, [], null, Keys()));
        Assert.Throws<ArgumentNullException>(() => GovernanceAuditChainVerifier.Verify(Scope, null!, null, Keys()));
        Assert.Throws<ArgumentNullException>(() => GovernanceAuditChainVerifier.Verify(Scope, [], null, null!));
    }

    private static void AssertBreak(GovernanceAuditChainBreak actual, long position, GovernanceAuditBreakKind kind, SignedGovernanceAuditRecord record)
    {
        Assert.Equal(position, actual.Position);
        Assert.Equal(kind, actual.Kind);
        Assert.Equal(record.RecordId, actual.RecordId);
        Assert.Equal(record.Sequence, actual.RecordedSequence);
    }
}
