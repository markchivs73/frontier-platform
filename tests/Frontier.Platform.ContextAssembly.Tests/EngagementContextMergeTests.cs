using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>
/// S13.62 — <see cref="IEngagementContextStore"/>'s merge primitive: a refresh writes its own
/// named components and leaves every other key of the engagement's dynamic context intact.
/// <para>
/// <b>Why a merge and not an upsert.</b> <c>UpsertDynamicContextAsync</c> replaces the whole
/// document. An ADR-CR1 refresh is scoped — doc 18 §3 raises it for named components
/// (<c>engagement_profile</c>, <c>client_profile</c>), and doc 04 §8's signal carries
/// <c>changed_fields</c>, not a whole context. Routing a scoped refresh through the replacing
/// primitive deletes every key the refresh did not produce. The one that matters is
/// <c>engagement_brief</c>: <c>ContextContentFilter.cs:52</c> throws
/// <see cref="ContractViolationException"/> for a requested key the document does not hold, and
/// hard invariant 7 makes a contract violation a <em>permanent</em> failure — never retried. So
/// a single scoped refresh landing through an upsert would permanently kill every entry node
/// requesting the brief, on a live engagement, with no retry to recover it. That is the bug
/// these tests exist to prevent, and it is why the merge is a store primitive rather than a
/// read-modify-write the caller is trusted to get right.
/// </para>
/// <para>
/// <b>Byte-identity still governs the epoch.</b> ADR-EC1 — "no epoch bookkeeping; byte-identity
/// does the work" — and doc 04 §8 — "if bytes are identical, no new epoch" — apply to the merged
/// document as a whole. A merge that changes nothing must not invalidate a primed provider
/// cache.
/// </para>
/// <para>
/// <b>Signature.</b> The shape below is the minimum that expresses the semantics: the named
/// components to write, and the epoch the document is on afterwards. The <em>semantics</em> are
/// binding — write only these keys, preserve the rest byte-for-byte, no new epoch when nothing
/// changed. The member name and parameter shape are the implementer's to adjust, provided every
/// assertion here still holds against whatever replaces them.
/// </para>
/// </summary>
public sealed class EngagementContextMergeTests
{
    /// <summary>An engagement that already holds a brief plus an unrelated component, as a live engagement does.</summary>
    private const string SeededContext =
        """{"engagement_brief":"Draft the SOW for the Admin Website rebuild.","commercial_terms":{"currency":"GBP","day_rate":"950.00"}}""";

    private static Dictionary<string, string> RefreshedComponents() => new(StringComparer.Ordinal)
    {
        ["engagement_profile"] = """{"engagement_type":"advisory-sow","status":"active","data_quality":"enriched"}""",
        ["client_profile"] = """{"client_id":"client::acme","display_name":"Acme","data_quality":"enriched"}""",
    };

    /// <summary>
    /// The headline guarantee. Merging the two doc 18 §1 dynamic components into a document that
    /// already holds <c>engagement_brief</c> leaves that key byte-identical — not merely present,
    /// not re-serialized into an equivalent shape, but the same bytes, because the bytes are what
    /// the content hash and every provider cache key are taken over.
    /// </summary>
    [Fact]
    public async Task MergeDynamicContextAsync_PreservesUnnamedKeysByteIdentically()
    {
        var store = new Phase1EngagementContextStore();
        var engagementId = new EngagementId("eng-merge-preserve");
        await store.UpsertDynamicContextAsync(engagementId, SeededContext, CancellationToken.None);

        await store.MergeDynamicContextAsync(engagementId, RefreshedComponents(), CancellationToken.None);

        var merged = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        Assert.Equal(RawValue(SeededContext, "engagement_brief"), RawValue(merged!, "engagement_brief"));
        Assert.Equal(RawValue(SeededContext, "commercial_terms"), RawValue(merged!, "commercial_terms"));
    }

    /// <summary>
    /// The merge must actually land its own components — the preservation guarantee above is
    /// worthless if satisfied by writing nothing at all.
    /// </summary>
    [Fact]
    public async Task MergeDynamicContextAsync_WritesTheNamedComponents()
    {
        var store = new Phase1EngagementContextStore();
        var engagementId = new EngagementId("eng-merge-writes");
        await store.UpsertDynamicContextAsync(engagementId, SeededContext, CancellationToken.None);

        await store.MergeDynamicContextAsync(engagementId, RefreshedComponents(), CancellationToken.None);

        var merged = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        using var document = JsonDocument.Parse(merged!);
        Assert.Equal("advisory-sow", document.RootElement.GetProperty("engagement_profile").GetProperty("engagement_type").GetString());
        Assert.Equal("client::acme", document.RootElement.GetProperty("client_profile").GetProperty("client_id").GetString());
    }

    /// <summary>
    /// A component already present is replaced by the merge, not merged into recursively. The
    /// primitive's unit is the named component — doc 18 §2's <c>client_profile</c> renders whole,
    /// and a stub→enriched transition <em>removes</em> the <c>note</c> field, which a recursive
    /// merge would strip from the new bytes' meaning by leaving the stale key behind.
    /// </summary>
    [Fact]
    public async Task MergeDynamicContextAsync_ReplacesANamedComponentWholesale()
    {
        var store = new Phase1EngagementContextStore();
        var engagementId = new EngagementId("eng-merge-replace");
        await store.UpsertDynamicContextAsync(
            engagementId,
            """{"engagement_brief":"b","client_profile":{"client_id":"client::acme","data_quality":"stub","note":"CRM-derived fields unavailable."}}""",
            CancellationToken.None);

        await store.MergeDynamicContextAsync(engagementId, RefreshedComponents(), CancellationToken.None);

        var merged = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        using var document = JsonDocument.Parse(merged!);
        var clientProfile = document.RootElement.GetProperty("client_profile");
        Assert.Equal("enriched", clientProfile.GetProperty("data_quality").GetString());
        Assert.False(clientProfile.TryGetProperty("note", out _));
    }

    /// <summary>
    /// ADR-EC1's byte-identity rule. Merging components the document already holds, unchanged,
    /// produces no new epoch — the primed provider cache stays primed.
    /// </summary>
    [Fact]
    public async Task MergeDynamicContextAsync_ByteIdenticalContent_ProducesNoNewEpoch()
    {
        var store = new Phase1EngagementContextStore();
        var engagementId = new EngagementId("eng-merge-noop");
        await store.UpsertDynamicContextAsync(engagementId, SeededContext, CancellationToken.None);
        var afterFirstMerge = await store.MergeDynamicContextAsync(engagementId, RefreshedComponents(), CancellationToken.None);

        var afterSecondMerge = await store.MergeDynamicContextAsync(engagementId, RefreshedComponents(), CancellationToken.None);

        Assert.Equal(afterFirstMerge, afterSecondMerge);
        var current = await store.GetDynamicContextSnapshotAsync(engagementId, null, CancellationToken.None);
        Assert.Equal(afterSecondMerge, current!.Epoch);
    }

    /// <summary>A merge that genuinely changes bytes does advance the epoch — the no-op rule must be a comparison, not a refusal to write.</summary>
    [Fact]
    public async Task MergeDynamicContextAsync_ChangedContent_AdvancesTheEpoch()
    {
        var store = new Phase1EngagementContextStore();
        var engagementId = new EngagementId("eng-merge-advance");
        await store.UpsertDynamicContextAsync(engagementId, SeededContext, CancellationToken.None);
        var before = await store.MergeDynamicContextAsync(engagementId, RefreshedComponents(), CancellationToken.None);

        var changed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_profile"] = """{"client_id":"client::acme","display_name":"Acme Holdings","data_quality":"enriched"}""",
        };
        var after = await store.MergeDynamicContextAsync(engagementId, changed, CancellationToken.None);

        Assert.Equal(before + 1, after);
    }

    /// <summary>
    /// Merging into an engagement that holds no context yet is a create, not a failure — doc 04
    /// §10's "no dynamic context yet" row makes epoch 0 the initial-assembly outcome, and a
    /// refresh arriving first must not dead-letter on an engagement that simply has not assembled.
    /// </summary>
    [Fact]
    public async Task MergeDynamicContextAsync_NoExistingContext_CreatesAtEpochZero()
    {
        var store = new Phase1EngagementContextStore();
        var engagementId = new EngagementId("eng-merge-fresh");

        var epoch = await store.MergeDynamicContextAsync(engagementId, RefreshedComponents(), CancellationToken.None);

        Assert.Equal(0, epoch);
        var merged = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        using var document = JsonDocument.Parse(merged!);
        Assert.True(document.RootElement.TryGetProperty("engagement_profile", out _));
    }

    /// <summary>
    /// The merged document is canonical JSON. Everything downstream — the content hash on the
    /// run's evidence, the cache key, the signed audit record — is taken over these bytes, so a
    /// merge that emitted differently-shaped JSON than the profile produces would break hashing
    /// for every engagement it touched (hard invariant 1).
    /// </summary>
    [Fact]
    public async Task MergeDynamicContextAsync_ProducesCanonicalBytes()
    {
        var store = new Phase1EngagementContextStore();
        var engagementId = new EngagementId("eng-merge-canonical");
        await store.UpsertDynamicContextAsync(engagementId, SeededContext, CancellationToken.None);

        await store.MergeDynamicContextAsync(engagementId, RefreshedComponents(), CancellationToken.None);

        var merged = await store.GetDynamicContextAsync(engagementId, CancellationToken.None);
        var reserialized = JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(merged!), CanonicalProfile.Options);
        Assert.Equal(reserialized, merged);
        var snapshot = await store.GetDynamicContextSnapshotAsync(engagementId, null, CancellationToken.None);
        Assert.Equal(CanonicalProfile.Hash(merged!), snapshot!.ContentHash);
    }

    /// <summary>Returns the raw JSON text of <paramref name="key"/> in <paramref name="json"/> — the bytes, not a parsed equivalent.</summary>
    private static string RawValue(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(key).GetRawText();
    }
}
