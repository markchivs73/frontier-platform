using Frontier.Platform.Abstractions;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit.Tests;

/// <summary>S5.5 tests for <see cref="AuditSigner"/> (doc 05 §5, §2), extended at S13.106 for the guarded append (ADR-PA31).</summary>
public sealed class AuditSignerTests
{
    private static readonly DevKeyProvider KeyProvider = new();

    /// <summary>
    /// The signer under test. The fake is both the record store and the chain-head store, as
    /// <see cref="CosmosAuditRecordStore"/> is. Delays are zeroed so the retry tests do not sleep.
    /// </summary>
    private static AuditSigner Signer(FakeAuditRecordStore store, IKeyProvider? keyProvider = null, int maxAttempts = 8, IAuditSigningService? signingService = null) =>
        new(store, store, keyProvider ?? KeyProvider, signingService ?? new HmacAuditSigningService(keyProvider ?? KeyProvider), Options.Create(new ExecutionAuditOptions
        {
            AppendMaxAttempts = maxAttempts,
            AppendBaseDelayMs = 0,
            AppendMaxDelayMs = 0,
        }));

    [Fact]
    public async Task SignAsync_NullRecord_ThrowsArgumentNullException()
    {
        var signer = Signer(new FakeAuditRecordStore());

        await Assert.ThrowsAsync<ArgumentNullException>(() => signer.SignAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task SignAsync_NoPriorChain_ChainsFromGenesisHash()
    {
        var signer = Signer(new FakeAuditRecordStore());
        var record = AuditRecordHasherTests.Sample();

        var signed = await signer.SignAsync(record, CancellationToken.None);

        Assert.Equal(AuditRecordHasher.ComputeGenesisHash(record.EngagementId), signed.PreviousRecordHash);
        Assert.Equal(AuditRecordHasher.ComputeRecordHash(record, signed.PreviousRecordHash), signed.RecordHash);
    }

    [Fact]
    public async Task SignAsync_SetsSignatureAndSigningKeyIdFromCurrentKey()
    {
        var signer = Signer(new FakeAuditRecordStore());
        var key = await KeyProvider.GetCurrentKeyAsync(CancellationToken.None);

        var signed = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        Assert.Equal(key.KeyId, signed.SigningKeyId);
        Assert.Equal(AuditRecordHasher.ComputeSignature(signed.RecordHash, key.KeyMaterial), signed.Signature);
    }

    [Fact]
    public async Task SignAsync_PersistsViaRecordStore()
    {
        var store = new FakeAuditRecordStore();
        var signer = Signer(store);

        var signed = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        var chain = await store.GetChainAsync(signed.EngagementId, CancellationToken.None);
        Assert.Equal([signed], chain);
    }

    [Fact]
    public async Task SignAsync_SecondRecordForEngagement_ChainsFromFirstRecordsHash()
    {
        var store = new FakeAuditRecordStore();
        var signer = Signer(store);
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
        var signer = Signer(store);
        await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None));
    }

    [Fact]
    public async Task VerifyAsync_SignedRecord_ReturnsValid()
    {
        var store = new FakeAuditRecordStore();
        var signer = Signer(store);
        var signed = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        var result = await signer.VerifyAsync(signed.ExecutionId, signed.EngagementId, CancellationToken.None);

        Assert.True(result.SignatureValid);
        Assert.True(result.ChainValid);
        Assert.Null(result.BrokenLinkAt);
        Assert.Equal((await KeyProvider.GetCurrentKeyAsync(CancellationToken.None)).KeyId, result.VerifiedAgainstKeyId);
    }

    [Fact]
    public async Task VerifyAsync_TamperedStoredRecord_ReturnsBrokenChainAtThatExecution()
    {
        var store = new FakeAuditRecordStore();
        var signer = Signer(store);
        var signed = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);
        store.Replace(signed with { FinalStatus = ExecutionStatus.Failed });

        var result = await signer.VerifyAsync(signed.ExecutionId, signed.EngagementId, CancellationToken.None);

        Assert.False(result.SignatureValid);
        Assert.False(result.ChainValid);
        Assert.Equal(signed.ExecutionId, result.BrokenLinkAt);
    }

    [Fact]
    public async Task VerifyAsync_NoRecordForExecution_Throws()
    {
        var signer = Signer(new FakeAuditRecordStore());

        await Assert.ThrowsAsync<InvalidOperationException>(() => signer.VerifyAsync("eng-1::wf-1", "eng-1", CancellationToken.None));
    }

    [Fact]
    public async Task VerifyAsync_PostRotation_VerifiedAgainstKeyIdIsTheRecordsKey()
    {
        // S13.66 / doc 05 §5: a v1 record verified after rotation to v2 verifies under v1, forever.
        var store = new FakeAuditRecordStore();
        var provider = new FakeRotatingKeyProvider();
        var signer = Signer(store, provider);
        var signed = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);
        provider.RotateTo(FakeRotatingKeyProvider.V2);

        var result = await signer.VerifyAsync(signed.ExecutionId, signed.EngagementId, CancellationToken.None);

        Assert.True(result.SignatureValid);
        Assert.True(result.ChainValid);
        Assert.Equal(FakeRotatingKeyProvider.V1.KeyId, result.VerifiedAgainstKeyId);
        Assert.Null(result.UnresolvedKeyIds);
    }

    [Fact]
    public async Task VerifyAsync_ChainSpanningTwoKeys_VerifiesEndToEnd()
    {
        var store = new FakeAuditRecordStore();
        var provider = new FakeRotatingKeyProvider();
        var signer = Signer(store, provider);
        var first = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);
        provider.RotateTo(FakeRotatingKeyProvider.V2);
        var second = await signer.SignAsync(
            AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2", ClosedAtUtc = first.ClosedAtUtc.AddMinutes(1) },
            CancellationToken.None);

        Assert.Equal(FakeRotatingKeyProvider.V2.KeyId, second.SigningKeyId);
        foreach (var record in new[] { first, second })
        {
            var result = await signer.VerifyAsync(record.ExecutionId, record.EngagementId, CancellationToken.None);

            Assert.True(result.SignatureValid);
            Assert.True(result.ChainValid);
            Assert.Null(result.BrokenLinkAt);
            Assert.Equal(record.SigningKeyId, result.VerifiedAgainstKeyId);
        }
    }

    [Fact]
    public async Task VerifyAsync_TargetKeyVersionDestroyed_FailsClosedAndReportsTheUnresolvedId()
    {
        // Decision (A): indistinguishable-from-forgery is not acceptable — the id is reported;
        // decision (C): hash continuity is key-free, so the chain is still walked and still valid.
        var store = new FakeAuditRecordStore();
        var provider = new FakeRotatingKeyProvider();
        var signer = Signer(store, provider);
        var first = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);
        provider.RotateTo(FakeRotatingKeyProvider.V2);
        await signer.SignAsync(
            AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2", ClosedAtUtc = first.ClosedAtUtc.AddMinutes(1) },
            CancellationToken.None);
        provider.Forget(FakeRotatingKeyProvider.V1.KeyId);

        var result = await signer.VerifyAsync(first.ExecutionId, first.EngagementId, CancellationToken.None);

        Assert.False(result.SignatureValid);
        Assert.Equal(FakeRotatingKeyProvider.V1.KeyId, result.VerifiedAgainstKeyId);
        Assert.Equal([FakeRotatingKeyProvider.V1.KeyId], result.UnresolvedKeyIds);
        Assert.True(result.ChainValid);
        Assert.Null(result.BrokenLinkAt);
    }

    [Fact]
    public async Task VerifyAsync_OtherRecordsKeyDestroyed_TargetStillVerifiesAndTheIdIsReported()
    {
        var store = new FakeAuditRecordStore();
        var provider = new FakeRotatingKeyProvider();
        var signer = Signer(store, provider);
        var first = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);
        provider.RotateTo(FakeRotatingKeyProvider.V2);
        var second = await signer.SignAsync(
            AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2", ClosedAtUtc = first.ClosedAtUtc.AddMinutes(1) },
            CancellationToken.None);
        provider.Forget(FakeRotatingKeyProvider.V1.KeyId);

        var result = await signer.VerifyAsync(second.ExecutionId, second.EngagementId, CancellationToken.None);

        Assert.True(result.SignatureValid);
        Assert.Equal(FakeRotatingKeyProvider.V2.KeyId, result.VerifiedAgainstKeyId);
        Assert.Equal([FakeRotatingKeyProvider.V1.KeyId], result.UnresolvedKeyIds);
        Assert.True(result.ChainValid);
    }

    [Fact]
    public async Task SignAsync_FirstRecordForEngagement_CreatesTheHeadFromGenesis()
    {
        var store = new FakeAuditRecordStore();

        var signed = await Signer(store).SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        var head = store.Head!;
        Assert.Equal(1, head.Sequence);
        Assert.Equal(signed.RecordHash, head.RecordHash);
        Assert.Equal(signed.ExecutionId, head.LastExecutionId);
        Assert.Equal(0, head.UnguardedRecordCount);
    }

    [Fact]
    public async Task SignAsync_HeadMovedBetweenReadAndWrite_RereadsRehashesAndChainsFromTheWinner()
    {
        // The S13.106 defect itself: two executions on one engagement closing together. The loser
        // must re-anchor on the winner rather than chain from the same predecessor.
        var store = new FakeAuditRecordStore();
        var signer = Signer(store);
        SignedAuditRecord? competitor = null;
        store.BeforeAppend = async () =>
        {
            store.BeforeAppend = null;
            competitor = await signer.SignAsync(
                AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-competitor", WorkflowId = "wf-competitor" },
                CancellationToken.None);
        };

        var mine = await signer.SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None);

        Assert.Equal(1, store.ConflictsReturned);
        Assert.Equal(competitor!.RecordHash, mine.PreviousRecordHash);
        Assert.Equal(2, store.Head!.Sequence);
        var result = await signer.VerifyAsync(mine.ExecutionId, mine.EngagementId, CancellationToken.None);
        Assert.True(result.ChainValid);
        Assert.Null(result.Forks);
    }

    [Fact]
    public async Task SignAsync_ParallelClosesOnOneEngagement_GiveALinearChainWithNoDuplicatePredecessor()
    {
        const int count = 12;
        var store = new FakeAuditRecordStore { YieldBeforeWrite = true };
        var signer = Signer(store, maxAttempts: 50);

        await Task.WhenAll(Enumerable.Range(1, count).Select(index => Task.Run(() => signer.SignAsync(
            AuditRecordHasherTests.Sample() with
            {
                ExecutionId = $"eng-1::wf-{index}",
                WorkflowId = $"wf-{index}",
                ClosedAtUtc = AuditRecordHasherTests.Sample().ClosedAtUtc.AddMinutes(index),
            },
            CancellationToken.None))));

        var chain = await store.GetChainAsync("eng-1", CancellationToken.None);
        Assert.Equal(count, chain.Count);
        Assert.Equal(count, chain.Select(record => record.PreviousRecordHash).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(count, store.Head!.Sequence);

        // The binding property, asserted through the public verifier: closes land in head-race
        // order, which has no relation to closed_at_utc, and the chain must still verify clean
        // because verification follows the hash links (ADR-PA31, Mark's decision 2026-09-16).
        var result = await signer.VerifyAsync(chain[0].ExecutionId, "eng-1", CancellationToken.None);
        Assert.True(result.ChainValid);
        Assert.Null(result.BrokenLinkAt);
        Assert.Null(result.Forks);
        Assert.Null(result.UnreachableRecords);
    }

    [Fact]
    public async Task SignAsync_ChainPredatingTheGuard_CreatesTheHeadFromTheStoredTail()
    {
        // ADR-PA31 migration: an engagement whose records were written before the head existed. The
        // first append after the upgrade must chain from the stored tail and create the head from it
        // — no backfill job, nothing stored rewritten.
        var store = new FakeAuditRecordStore();
        var (first, second) = SeedUnguardedChain(store);

        var signed = await Signer(store).SignAsync(
            AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-3", WorkflowId = "wf-3", ClosedAtUtc = second.ClosedAtUtc.AddMinutes(1) },
            CancellationToken.None);

        Assert.Equal(second.RecordHash, signed.PreviousRecordHash);
        var head = store.Head!;
        Assert.Equal(3, head.Sequence);
        Assert.Equal(2, head.UnguardedRecordCount);
        Assert.Equal(first.RecordHash, second.PreviousRecordHash);
    }

    [Fact]
    public async Task SignAsync_SecondAppendAfterMigration_CarriesTheUnguardedCountForward()
    {
        // The count is frozen at head creation: it must not drift as the chain grows, or a later
        // legacy fork would be misclassified as a guarded one.
        var store = new FakeAuditRecordStore();
        SeedUnguardedChain(store);
        var signer = Signer(store);
        await signer.SignAsync(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-3", WorkflowId = "wf-3" }, CancellationToken.None);

        await signer.SignAsync(AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-4", WorkflowId = "wf-4" }, CancellationToken.None);

        Assert.Equal(4, store.Head!.Sequence);
        Assert.Equal(2, store.Head!.UnguardedRecordCount);
    }

    [Fact]
    public async Task SignAsync_PreGuardForkStillInTheChain_VerifiesAsLegacyForkNotAsTampering()
    {
        // The stored fork is never rewritten and never re-signed; verification names it for what it is.
        var store = new FakeAuditRecordStore();
        var genesis = AuditRecordHasher.ComputeGenesisHash("eng-1");
        var first = AuditChainVerifierTests.Sign(AuditRecordHasherTests.Sample(), genesis, FakeRotatingKeyProvider.V1);
        var second = AuditChainVerifierTests.Sign(
            AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2", ClosedAtUtc = first.ClosedAtUtc.AddMinutes(1) },
            genesis,
            FakeRotatingKeyProvider.V1);
        store.SeedUnguarded(first);
        store.SeedUnguarded(second);

        var result = await Signer(store, new FakeRotatingKeyProvider()).VerifyAsync(second.ExecutionId, "eng-1", CancellationToken.None);

        Assert.Equal(AuditChainForkKind.Legacy, Assert.Single(result.Forks!).Kind);
        Assert.True(result.SignatureValid);
        Assert.Null(result.UnresolvedKeyIds);
    }

    [Fact]
    public async Task SignAsync_ConflictsExhaustTheConfiguredAttempts_ThrowsAndStoresNothing()
    {
        var store = new FakeAuditRecordStore { AlwaysConflict = true };

        var exception = await Assert.ThrowsAsync<AuditChainAppendException>(() =>
            Signer(store, maxAttempts: 3).SignAsync(AuditRecordHasherTests.Sample(), CancellationToken.None));

        Assert.Equal(3, store.AppendCalls);
        Assert.Contains("NOT stored", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await store.GetChainAsync("eng-1", CancellationToken.None));
    }

    /// <summary>Seeds two correctly-linked records with no head — a chain as it stood before ADR-PA31.</summary>
    private static (SignedAuditRecord First, SignedAuditRecord Second) SeedUnguardedChain(FakeAuditRecordStore store)
    {
        var first = AuditChainVerifierTests.Sign(AuditRecordHasherTests.Sample(), AuditRecordHasher.ComputeGenesisHash("eng-1"), FakeRotatingKeyProvider.V1);
        var second = AuditChainVerifierTests.Sign(
            AuditRecordHasherTests.Sample() with { ExecutionId = "eng-1::wf-2", WorkflowId = "wf-2", ClosedAtUtc = first.ClosedAtUtc.AddMinutes(1) },
            first.RecordHash,
            FakeRotatingKeyProvider.V1);
        store.SeedUnguarded(first);
        store.SeedUnguarded(second);
        return (first, second);
    }
}
