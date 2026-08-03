namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>Tests for <see cref="Phase1EngagementContextStore"/> (S4.2).</summary>
public sealed class Phase1EngagementContextStoreTests
{
    [Fact]
    public async Task GetDynamicContextAsync_SeededEngagement_ReturnsEngagementContextJson()
    {
        var store = new Phase1EngagementContextStore();

        var json = await store.GetDynamicContextAsync(Phase1ContextCatalogue.SeedEngagementId, CancellationToken.None);

        Assert.Equal(Phase1ContextCatalogue.EngagementContextJson[Phase1ContextCatalogue.SeedEngagementId], json);
    }

    [Fact]
    public async Task GetDynamicContextAsync_UnknownEngagement_ReturnsNull()
    {
        var store = new Phase1EngagementContextStore();

        var json = await store.GetDynamicContextAsync("unknown-engagement", CancellationToken.None);

        Assert.Null(json);
    }

    [Fact]
    public async Task GetDynamicContextAsync_SandboxEngagement_ReturnsDefaultBrief()
    {
        // S9.83: the sandbox test-run channel mints ephemeral SANDBOX-{guid} engagements that are
        // never seeded (and can't be, cross-process). The PoC store hands them a default brief so the
        // entry node's EngagementBriefSection bridging resolves and the run can execute.
        var store = new Phase1EngagementContextStore();

        var json = await store.GetDynamicContextAsync($"{Phase1EngagementContextStore.SandboxEngagementPrefix}abc123", CancellationToken.None);

        Assert.NotNull(json);
        Assert.Contains(Phase1EngagementContextStore.SandboxDefaultBrief, json, StringComparison.Ordinal);
        Assert.Contains("engagement_brief", json!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDynamicContextAsync_SeededSandboxEngagement_ReturnsSeededOverDefault()
    {
        // An explicit upsert wins over the sandbox default (the seeded value is returned).
        var store = new Phase1EngagementContextStore();
        var id = $"{Phase1EngagementContextStore.SandboxEngagementPrefix}seeded";
        await store.UpsertDynamicContextAsync(id, """{"engagement_brief":"explicit"}""", CancellationToken.None);

        var json = await store.GetDynamicContextAsync(id, CancellationToken.None);

        Assert.Equal("""{"engagement_brief":"explicit"}""", json);
    }

    [Fact]
    public async Task GetDynamicContextAsync_WhitespaceEngagementId_Throws()
    {
        var store = new Phase1EngagementContextStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.GetDynamicContextAsync(" ", CancellationToken.None));
    }

    [Fact]
    public async Task UpsertDynamicContextAsync_NewEngagement_IsVisibleToLaterReads()
    {
        var store = new Phase1EngagementContextStore();

        await store.UpsertDynamicContextAsync("engagement-new", """{"engagement_brief": "new"}""", CancellationToken.None);
        var json = await store.GetDynamicContextAsync("engagement-new", CancellationToken.None);

        Assert.Equal("""{"engagement_brief": "new"}""", json);
    }

    [Fact]
    public async Task UpsertDynamicContextAsync_WhitespaceEngagementId_Throws()
    {
        var store = new Phase1EngagementContextStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertDynamicContextAsync(" ", "{}", CancellationToken.None));
    }

    [Fact]
    public async Task UpsertDynamicContextAsync_NullDynamicContent_Throws()
    {
        var store = new Phase1EngagementContextStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.UpsertDynamicContextAsync("engagement-new", null!, CancellationToken.None));
    }
}
