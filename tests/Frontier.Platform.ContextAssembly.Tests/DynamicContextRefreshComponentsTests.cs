using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>
/// S13.62 — <see cref="IDynamicContextRefresher.RefreshComponentsAsync"/> and the guard branches of
/// <see cref="EngagementContextMerge"/>. The binding suite pins the store's merge semantics and the
/// epoch the refresher reports; these cover the branches those tests do not reach, so the
/// per-assembly line+branch gate stays satisfied without weakening anything.
/// </summary>
public sealed class DynamicContextRefreshComponentsTests
{
    private const string Reason = "CRM_pricing_changed";

    private static Dictionary<string, string> Components() => new(StringComparer.Ordinal)
    {
        ["client_profile"] = """{"client_id":"client::acme","data_quality":"enriched"}""",
    };

    private static DynamicContextRefresher Refresher(IEngagementContextStore store) =>
        new(store, NullLogger<DynamicContextRefresher>.Instance);

    /// <summary>A component refresh on a fresh engagement creates at epoch 0 and reports the merged document's hash.</summary>
    [Fact]
    public async Task RefreshComponentsAsync_NoExistingContext_CreatesAndReportsRefreshed()
    {
        var store = new Phase1EngagementContextStore();
        using var refresher = Refresher(store);

        var result = await refresher.RefreshComponentsAsync(new EngagementId("eng-rc-new"), Components(), Reason, CancellationToken.None);

        Assert.True(result.Refreshed);
        Assert.Equal(0, result.Epoch);
        var stored = await store.GetDynamicContextAsync(new EngagementId("eng-rc-new"), CancellationToken.None);
        Assert.Equal(CanonicalProfile.Hash(stored!), result.ContentHash);
    }

    /// <summary>
    /// The no-op branch: merging components the document already holds reports
    /// <c>Refreshed: false</c> on the unchanged epoch. This is the reporting path the epoch-0
    /// defect lived on, now exercised through the component API too.
    /// </summary>
    [Fact]
    public async Task RefreshComponentsAsync_IdenticalComponents_ReportsNotRefreshedOnTheCurrentEpoch()
    {
        var store = new Phase1EngagementContextStore();
        using var refresher = Refresher(store);
        var engagementId = new EngagementId("eng-rc-noop");
        await store.UpsertDynamicContextAsync(engagementId, """{"engagement_brief":"b"}""", CancellationToken.None);
        var first = await refresher.RefreshComponentsAsync(engagementId, Components(), Reason, CancellationToken.None);

        var second = await refresher.RefreshComponentsAsync(engagementId, Components(), Reason, CancellationToken.None);

        Assert.True(first.Refreshed);
        Assert.False(second.Refreshed);
        Assert.Equal(first.Epoch, second.Epoch);
        Assert.Equal(first.ContentHash, second.ContentHash);
    }

    /// <summary>A component refresh leaves keys it does not name intact — the guarantee, reached through the refresher rather than the store.</summary>
    [Fact]
    public async Task RefreshComponentsAsync_PreservesKeysItDoesNotName()
    {
        var store = new Phase1EngagementContextStore();
        using var refresher = Refresher(store);
        var engagementId = new EngagementId("eng-rc-preserve");
        await store.UpsertDynamicContextAsync(engagementId, """{"engagement_brief":"Draft the SOW."}""", CancellationToken.None);

        await refresher.RefreshComponentsAsync(engagementId, Components(), Reason, CancellationToken.None);

        var merged = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        Assert.Contains("engagement_brief", merged!, StringComparison.Ordinal);
    }

    /// <summary>ADR-CR1's reason is required here exactly as it is on the whole-document refresh.</summary>
    [Fact]
    public async Task RefreshComponentsAsync_BlankReason_Throws()
    {
        using var refresher = Refresher(new Phase1EngagementContextStore());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            refresher.RefreshComponentsAsync(new EngagementId("eng-rc-reason"), Components(), "  ", CancellationToken.None));
    }

    /// <summary>The guard branches at the refresher's public boundary.</summary>
    [Fact]
    public async Task RefreshComponentsAsync_NullArguments_Throw()
    {
        using var refresher = Refresher(new Phase1EngagementContextStore());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            refresher.RefreshComponentsAsync(null!, Components(), Reason, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            refresher.RefreshComponentsAsync(new EngagementId("eng-rc-null"), null!, Reason, CancellationToken.None));
    }

    /// <summary>Merging nothing into an existing document is a no-op, not a wipe — the empty-map case the port's contract promises is safe.</summary>
    [Fact]
    public void Apply_NoComponents_LeavesTheDocumentUnchanged()
    {
        const string current = """{"engagement_brief":"b"}""";

        var merged = EngagementContextMerge.Apply(current, new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Equal(current, merged);
    }

    /// <summary>Guard branches on the merge helpers' public boundary.</summary>
    [Fact]
    public async Task Merge_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => EngagementContextMerge.Apply("{}", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            EngagementContextMerge.ApplyThroughAsync(null!, new EngagementId("eng-rc-guard"), Components(), CancellationToken.None));
    }
}
