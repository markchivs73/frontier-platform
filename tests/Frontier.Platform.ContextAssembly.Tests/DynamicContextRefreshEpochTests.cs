using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>
/// S13.62 — the epoch a refresh reports back, and the epoch-0 no-op defect.
/// <para>
/// <b>The defect.</b> <c>DynamicContextRefresher.cs:57</c> returns <c>Epoch: 0</c> on the
/// identical-bytes no-op path. That constant is not "no epoch", it is a valid epoch — the
/// <em>first</em> one, since <c>UpsertDynamicContextAsync</c> is 0-based. S13.60 made this
/// reachable and harmful in the same change: a run's pin now lives in
/// <c>GraphExecutionState.DynamicContextEpoch</c>, and S13.62 is the first caller that assigns a
/// refresh result to it. A caller that refreshes at epoch 4, finds the bytes unchanged, and
/// stores the returned <c>0</c> has silently dragged the run's pin back four epochs — and
/// because <c>GetDynamicContextSnapshotAsync</c> serves exactly the epoch asked for, every
/// subsequent assembly then reads stale bytes while the snapshot and the signed audit record
/// both attest to epoch 0. Wrong provenance is worse than none: it is evidence that reads as
/// true.
/// </para>
/// <para>
/// <b>The fix this binds.</b> A no-op returns the store's <em>current</em> epoch. The refresher
/// already has the means — <c>IEngagementContextStore.GetDynamicContextSnapshotAsync(id, null,
/// ct)</c> returns the current version with its <c>Epoch</c> — so no new store surface is
/// needed. The no-op semantics themselves are unchanged and must stay unchanged: identical bytes
/// still write nothing and still report <c>Refreshed: false</c>, because ADR-EC1 and doc 04 §8
/// both turn on byte-identity meaning "no new epoch, no needless cache invalidation".
/// </para>
/// <para>
/// The pre-existing <c>DynamicContextRefresherTests</c> cases are untouched and stay green: each
/// seeds exactly one upsert, so the store's current epoch there genuinely <em>is</em> 0 and the
/// old assertion is accidentally right. They never distinguish "the current epoch" from "the
/// constant zero", which is precisely why the defect survived them. These cases move the store
/// off epoch 0 first, so only the correct answer passes.
/// </para>
/// </summary>
public sealed class DynamicContextRefreshEpochTests
{
    private const string Reason = "CRM_pricing_changed";

    /// <summary>
    /// The regression pin. The store is walked to epoch 2, then refreshed with byte-identical
    /// content: the result must report epoch 2 — the version the engagement is actually on —
    /// and must not report 0.
    /// </summary>
    [Fact]
    public async Task RefreshDynamicAsync_IdenticalBytesAtNonZeroEpoch_ReturnsCurrentEpochNotZero()
    {
        var store = new Phase1EngagementContextStore();
        using var refresher = new DynamicContextRefresher(store, NullLogger<DynamicContextRefresher>.Instance);
        var engagementId = new EngagementId("eng-epoch-noop");

        // Three writes of differing bytes: the 0-based counter lands on epoch 2.
        await store.UpsertDynamicContextAsync(engagementId, """{"v":1}""", CancellationToken.None);
        await store.UpsertDynamicContextAsync(engagementId, """{"v":2}""", CancellationToken.None);
        var currentEpoch = await store.UpsertDynamicContextAsync(engagementId, """{"v":3}""", CancellationToken.None);
        Assert.Equal(2, currentEpoch);

        var result = await refresher.RefreshDynamicAsync(engagementId, """{"v":3}""", Reason, CancellationToken.None);

        Assert.False(result.Refreshed);
        Assert.Equal(currentEpoch, result.Epoch);
        Assert.NotEqual(0, result.Epoch);
    }

    /// <summary>
    /// A no-op must still write nothing. Reporting the current epoch is a reporting fix, not a
    /// licence to bump the version — ADR-EC1's byte-identity rule is what keeps provider caches
    /// primed across the stub→enriched transition.
    /// </summary>
    [Fact]
    public async Task RefreshDynamicAsync_IdenticalBytes_DoesNotAdvanceTheStoresEpoch()
    {
        var store = new Phase1EngagementContextStore();
        using var refresher = new DynamicContextRefresher(store, NullLogger<DynamicContextRefresher>.Instance);
        var engagementId = new EngagementId("eng-epoch-stable");
        await store.UpsertDynamicContextAsync(engagementId, """{"v":1}""", CancellationToken.None);
        var before = await store.UpsertDynamicContextAsync(engagementId, """{"v":2}""", CancellationToken.None);

        await refresher.RefreshDynamicAsync(engagementId, """{"v":2}""", Reason, CancellationToken.None);

        var after = await store.GetDynamicContextSnapshotAsync(engagementId, null, CancellationToken.None);
        Assert.Equal(before, after!.Epoch);
    }

    /// <summary>
    /// The changed-bytes path: a new epoch, <c>Refreshed: true</c>, and the hash of the new
    /// content. This is the half that already worked, pinned beside the fix so a change to the
    /// no-op branch cannot quietly alter it.
    /// </summary>
    [Fact]
    public async Task RefreshDynamicAsync_DifferingBytes_AdvancesTheEpochAndReportsRefreshed()
    {
        var store = new Phase1EngagementContextStore();
        using var refresher = new DynamicContextRefresher(store, NullLogger<DynamicContextRefresher>.Instance);
        var engagementId = new EngagementId("eng-epoch-move");
        await store.UpsertDynamicContextAsync(engagementId, """{"v":1}""", CancellationToken.None);
        var before = await store.UpsertDynamicContextAsync(engagementId, """{"v":2}""", CancellationToken.None);

        var result = await refresher.RefreshDynamicAsync(engagementId, """{"v":3}""", Reason, CancellationToken.None);

        Assert.True(result.Refreshed);
        Assert.Equal(before + 1, result.Epoch);
        Assert.Equal(CanonicalProfile.Hash("""{"v":3}"""), result.ContentHash);
    }

    /// <summary>
    /// The epoch a no-op reports must be the one a pinned read can actually resolve. This is the
    /// consequence the defect breaks and the reason the constant mattered: the returned epoch is
    /// handed straight to <c>GetDynamicContextSnapshotAsync</c> by every later assembly on the
    /// run, and the stores answer <see langword="null"/> for an epoch they do not hold rather
    /// than silently serving the latest.
    /// </summary>
    [Fact]
    public async Task RefreshDynamicAsync_IdenticalBytes_ReturnedEpochResolvesToTheCurrentBytes()
    {
        var store = new Phase1EngagementContextStore();
        using var refresher = new DynamicContextRefresher(store, NullLogger<DynamicContextRefresher>.Instance);
        var engagementId = new EngagementId("eng-epoch-resolves");
        await store.UpsertDynamicContextAsync(engagementId, """{"v":1}""", CancellationToken.None);
        await store.UpsertDynamicContextAsync(engagementId, """{"v":2}""", CancellationToken.None);

        var result = await refresher.RefreshDynamicAsync(engagementId, """{"v":2}""", Reason, CancellationToken.None);

        var pinned = await store.GetDynamicContextSnapshotAsync(engagementId, result.Epoch, CancellationToken.None);
        Assert.NotNull(pinned);
        Assert.Equal("""{"v":2}""", pinned!.Content);
        Assert.Equal(result.ContentHash, pinned.ContentHash);
    }

    /// <summary>
    /// First write for an engagement that holds nothing: epoch 0 is the honest answer here, and
    /// the fix must not turn "the current epoch happens to be 0" into an error or an off-by-one.
    /// </summary>
    [Fact]
    public async Task RefreshDynamicAsync_FirstWrite_ReportsEpochZero()
    {
        var store = new Phase1EngagementContextStore();
        using var refresher = new DynamicContextRefresher(store, NullLogger<DynamicContextRefresher>.Instance);

        var result = await refresher.RefreshDynamicAsync(new EngagementId("eng-epoch-first"), """{"initial":true}""", Reason, CancellationToken.None);

        Assert.True(result.Refreshed);
        Assert.Equal(0, result.Epoch);
    }
}
