using Frontier.Platform.Abstractions;
using Microsoft.Extensions.Options;
using static Frontier.Platform.Audit.Tests.GovernanceAuditSamples;

namespace Frontier.Platform.Audit.Tests;

/// <summary>S13.103 tests for <see cref="GovernanceAuditService"/> (ADR-PA30): fail-closed append, idempotency, compensation, the 412 retry and concurrency.</summary>
public sealed class GovernanceAuditServiceTests
{
    private readonly FakeGovernanceAuditStore store = new();
    private readonly FakeRotatingKeyProvider keys = new();

    [Fact]
    public async Task AppendAsync_FirstEntry_ChainsFromTheGovernanceGenesis()
    {
        var record = await Service().AppendAsync(Entry(), CancellationToken.None);

        Assert.Equal(1, record.Sequence);
        Assert.Equal(Genesis, record.PreviousRecordHash);
        Assert.Equal(FakeRotatingKeyProvider.V1.KeyId, record.SigningKeyId);
        Assert.Equal(GovernanceAuditHasher.ComputeRecordId(Entry()), record.RecordId);
        Assert.Same(record, Assert.Single(store.Records));
    }

    [Fact]
    public async Task AppendAsync_SecondEntry_LinksToTheFirst()
    {
        var service = Service();
        var first = await service.AppendAsync(Entry("role-a"), CancellationToken.None);

        var second = await service.AppendAsync(Entry("role-b"), CancellationToken.None);

        Assert.Equal(2, second.Sequence);
        Assert.Equal(first.RecordHash, second.PreviousRecordHash);
    }

    [Fact]
    public async Task AppendAsync_IdenticalEntryRetried_ReturnsTheStoredRecordWithoutAppending()
    {
        var service = Service();
        var first = await service.AppendAsync(Entry(), CancellationToken.None);

        var retried = await service.AppendAsync(Entry(), CancellationToken.None);

        Assert.Same(first, retried);
        Assert.Single(store.Records);
        Assert.Equal(1, store.AppendCalls);
    }

    [Fact]
    public async Task AppendAsync_InvalidEntry_ThrowsPermanentContractViolationWithoutTouchingTheStore()
    {
        var exception = await Assert.ThrowsAsync<ContractViolationException>(() =>
            Service().AppendAsync(Entry() with { Actor = "unknown" }, CancellationToken.None));

        Assert.Equal(nameof(GovernanceAuditEntry), exception.ContractType);
        Assert.Equal(0, store.ReadCalls);
        Assert.Equal(0, store.AppendCalls);
    }

    [Fact]
    public async Task AppendAsync_NullEntry_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service().AppendAsync(null!, CancellationToken.None));

    [Fact]
    public async Task AppendAsync_CompensatingEntry_AppendsAfterTheOriginalAndTheChainVerifies()
    {
        var service = Service();
        var original = await service.AppendAsync(Entry(), CancellationToken.None);
        var compensation = Entry(reason: "role store write failed after the audit landed") with
        {
            EventType = "approver_role_update_aborted",
            CompensatesRecordId = original.RecordId,
        };

        var compensating = await service.AppendAsync(compensation, CancellationToken.None);
        var verification = await service.VerifyAsync(GovernanceAuditScopes.Deployment, CancellationToken.None);

        Assert.Equal(original.RecordHash, compensating.PreviousRecordHash);
        Assert.Equal(original.RecordId, compensating.CompensatesRecordId);
        Assert.True(verification.Valid);
    }

    [Fact]
    public async Task AppendAsync_CompensatesARecordThatDoesNotExist_ThrowsContractViolation()
    {
        var entry = Entry() with { CompensatesRecordId = "NOT-A-RECORD" };

        var exception = await Assert.ThrowsAsync<ContractViolationException>(() => Service().AppendAsync(entry, CancellationToken.None));

        Assert.Contains("NOT-A-RECORD", Assert.Single(exception.Violations), StringComparison.Ordinal);
        Assert.Equal(0, store.AppendCalls);
    }

    [Fact]
    public async Task AppendAsync_HeadMovedBetweenReadAndWrite_RereadsRehashesAndLands()
    {
        var service = Service();
        store.BeforeAppend = async () =>
        {
            store.BeforeAppend = null;
            await service.AppendAsync(Entry("competitor"), CancellationToken.None);
        };

        var record = await service.AppendAsync(Entry("mine"), CancellationToken.None);

        Assert.Equal(1, store.ConflictsReturned);
        Assert.Equal(2, record.Sequence);
        Assert.Equal(store.Records[0].RecordHash, record.PreviousRecordHash);
        Assert.True((await service.VerifyAsync(GovernanceAuditScopes.Deployment, CancellationToken.None)).Valid);
    }

    [Fact]
    public async Task AppendAsync_ConflictsExhaustTheConfiguredAttempts_ThrowsAppendException()
    {
        store.AlwaysConflict = true;

        await Assert.ThrowsAsync<GovernanceAuditAppendException>(() => Service(maxAttempts: 3).AppendAsync(Entry(), CancellationToken.None));

        Assert.Equal(3, store.AppendCalls);
        Assert.Empty(store.Records);
    }

    [Fact]
    public async Task AppendAsync_ParallelAppendsOnOneScope_GiveAGaplessValidChain()
    {
        const int count = 16;
        var service = Service(maxAttempts: 50);
        store.YieldBeforeWrite = true;

        await Task.WhenAll(Enumerable.Range(1, count).Select(index => Task.Run(() => service.AppendAsync(Entry($"role-{index}"), CancellationToken.None))));

        var chain = store.Records.OrderBy(record => record.Sequence).ToList();
        Assert.Equal(Enumerable.Range(1, count).Select(index => (long)index), chain.Select(record => record.Sequence));
        Assert.Equal(count, chain.Select(record => record.PreviousRecordHash).Distinct(StringComparer.Ordinal).Count());
        Assert.True((await service.VerifyAsync(GovernanceAuditScopes.Deployment, CancellationToken.None)).Valid);
    }

    [Fact]
    public async Task AppendAsync_SignsWithTheGovernancePurposeKey()
    {
        var ring = new RecordingKeyRing(keys);

        var record = await new GovernanceAuditService(store, ring, Options.Create(new GovernanceAuditOptions())).AppendAsync(Entry(), CancellationToken.None);

        Assert.Equal(FakeRotatingKeyProvider.V1.KeyId, record.SigningKeyId);
        Assert.All(ring.Purposes, purpose => Assert.Equal(SigningKeyPurpose.GovernanceAudit, purpose));
    }

    [Fact]
    public async Task VerifyAsync_AfterRotation_OldRecordsVerifyAndADestroyedVersionFailsClosed()
    {
        var service = Service();
        await service.AppendAsync(Entry("role-a"), CancellationToken.None);
        keys.RotateTo(FakeRotatingKeyProvider.V2);
        var afterRotation = await service.AppendAsync(Entry("role-b"), CancellationToken.None);

        var rotated = await service.VerifyAsync(GovernanceAuditScopes.Deployment, CancellationToken.None);
        keys.Forget(FakeRotatingKeyProvider.V1.KeyId);
        var destroyed = await service.VerifyAsync(GovernanceAuditScopes.Deployment, CancellationToken.None);

        Assert.Equal(FakeRotatingKeyProvider.V2.KeyId, afterRotation.SigningKeyId);
        Assert.True(rotated.Valid);
        Assert.False(destroyed.Valid);
        Assert.Equal(GovernanceAuditBreakKind.UnresolvedKey, Assert.Single(destroyed.Breaks!).Kind);
    }

    [Fact]
    public async Task VerifyAsync_ScopeWithNothingRecorded_IsValid()
    {
        var result = await Service().VerifyAsync("empty_scope", CancellationToken.None);

        Assert.True(result.Valid);
        Assert.Null(result.HeadSequence);
    }

    [Fact]
    public async Task GetAsync_ReturnsTheRecordById()
    {
        var service = Service();
        var record = await service.AppendAsync(Entry(), CancellationToken.None);

        Assert.Same(record, await service.GetAsync(record.RecordId, CancellationToken.None));
        Assert.Null(await service.GetAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task QueryAsync_FiltersAndPages()
    {
        var service = Service();
        await service.AppendAsync(Entry("role-a"), CancellationToken.None);
        await service.AppendAsync(Entry("role-b"), CancellationToken.None);
        await service.AppendAsync(Entry("role-c") with { EventType = "approver_role_retired" }, CancellationToken.None);

        var first = await service.QueryAsync(new GovernanceAuditQuery { EventType = "approver_role_updated", PageSize = 1 }, CancellationToken.None);
        var second = await service.QueryAsync(new GovernanceAuditQuery { EventType = "approver_role_updated", PageSize = 1, ContinuationToken = first.ContinuationToken }, CancellationToken.None);

        Assert.Equal("role-a", Assert.Single(first.Records).SubjectId);
        Assert.Equal("role-b", Assert.Single(second.Records).SubjectId);
        Assert.Null(second.ContinuationToken);
    }

    [Fact]
    public async Task ArgumentGuards_RejectBadInput()
    {
        var service = Service();

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(" ", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.QueryAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.QueryAsync(new GovernanceAuditQuery { Scope = "" }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.QueryAsync(new GovernanceAuditQuery { PageSize = 0 }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.QueryAsync(new GovernanceAuditQuery { PageSize = GovernanceAuditQuery.MaxPageSize + 1 }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.VerifyAsync("", CancellationToken.None));
    }

    private GovernanceAuditService Service(int maxAttempts = 5) =>
        new(store, new SharedSigningKeyRing(keys, new HmacAuditSigningService(keys)), Options.Create(new GovernanceAuditOptions { AppendMaxAttempts = maxAttempts, AppendBaseDelayMs = 0, AppendMaxDelayMs = 0 }));

    private sealed class RecordingKeyRing(IKeyProvider provider) : ISigningKeyRing
    {
        internal List<SigningKeyPurpose> Purposes { get; } = [];

        public IKeyProvider GetProvider(SigningKeyPurpose purpose)
        {
            Purposes.Add(purpose);
            return provider;
        }

        public IAuditSigningService GetSigningService(SigningKeyPurpose purpose)
        {
            Purposes.Add(purpose);
            return new HmacAuditSigningService(provider);
        }
    }
}
