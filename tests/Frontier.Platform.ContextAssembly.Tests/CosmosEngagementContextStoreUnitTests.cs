using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>
/// Unit tests for <see cref="CosmosEngagementContextStore"/>'s decisions (S13.193): epoch numbering, document ids,
/// and what a write that loses the race for an epoch returns. The SDK half is <see cref="CosmosEngagementContextDocuments"/>,
/// covered by the emulator tests in <c>Integration/CosmosEngagementContextStoreTests</c>.
/// </summary>
public sealed class CosmosEngagementContextStoreUnitTests
{
    private static readonly EngagementId Engagement = "eng-1";

    [Fact]
    public void Ids_FollowTheDoc04Layout()
    {
        Assert.Equal("eng-1:ctx:current", CosmosEngagementContextStore.PointerId(Engagement));
        Assert.Equal("eng-1:ctx:e000042", CosmosEngagementContextStore.EpochId(Engagement, 42));
    }

    [Fact]
    public async Task UpsertDynamicContextAsync_FirstWrite_CreatesEpochZeroAndPointsAtIt()
    {
        var documents = new FakeContextDocuments();

        var epoch = await new CosmosEngagementContextStore(documents).UpsertDynamicContextAsync(Engagement, "{\"a\":1}", CancellationToken.None);

        Assert.Equal(0, epoch);
        var created = Assert.Single(documents.Epochs);
        Assert.Equal(("eng-1:ctx:e000000", 0, CanonicalProfile.Hash("{\"a\":1}")), (created.Id, created.Epoch, created.ContentHash));
        Assert.Equal(("eng-1:ctx:current", 0, created.ContentHash), (documents.Pointer!.Id, documents.Pointer.Epoch, documents.Pointer.ContentHash));
    }

    [Fact]
    public async Task UpsertDynamicContextAsync_ExistingContext_AppendsTheNextEpoch()
    {
        var documents = new FakeContextDocuments();
        var store = new CosmosEngagementContextStore(documents);
        await store.UpsertDynamicContextAsync(Engagement, "one", CancellationToken.None);

        var epoch = await store.UpsertDynamicContextAsync(Engagement, "two", CancellationToken.None);

        Assert.Equal(1, epoch);
        Assert.Equal(1, documents.Pointer!.Epoch);
        Assert.Equal(["one", "two"], documents.Epochs.Select(e => e.Content));
    }

    [Fact]
    public async Task UpsertDynamicContextAsync_LostTheRaceForTheEpoch_ReturnsThePointerEpochWithoutMovingIt()
    {
        var documents = new FakeContextDocuments { Pointer = Pointer(3), EpochTaken = true };

        var epoch = await new CosmosEngagementContextStore(documents).UpsertDynamicContextAsync(Engagement, "late", CancellationToken.None);

        Assert.Equal(3, epoch);
        Assert.Equal(3, documents.Pointer!.Epoch);
        Assert.Equal(0, documents.PointerWrites);
    }

    [Fact]
    public async Task UpsertDynamicContextAsync_LostTheFirstEpochBeforeThePointerMoved_ReturnsZero()
    {
        var documents = new FakeContextDocuments { EpochTaken = true };

        Assert.Equal(0, await new CosmosEngagementContextStore(documents).UpsertDynamicContextAsync(Engagement, "late", CancellationToken.None));
    }

    [Fact]
    public async Task GetDynamicContextSnapshotAsync_NoPointer_IsNull() =>
        Assert.Null(await new CosmosEngagementContextStore(new FakeContextDocuments()).GetDynamicContextSnapshotAsync(Engagement, null, CancellationToken.None));

    [Fact]
    public async Task GetDynamicContextSnapshotAsync_CurrentEpoch_ReadsTheEpochThePointerNames()
    {
        var documents = new FakeContextDocuments();
        var store = new CosmosEngagementContextStore(documents);
        await store.UpsertDynamicContextAsync(Engagement, "one", CancellationToken.None);
        await store.UpsertDynamicContextAsync(Engagement, "two", CancellationToken.None);

        var snapshot = await store.GetDynamicContextSnapshotAsync(Engagement, null, CancellationToken.None);

        Assert.Equal(new EngagementContextSnapshot(1, "eng-1:ctx:e000001", CanonicalProfile.Hash("two"), "two"), snapshot);
        Assert.Equal("two", await store.GetDynamicContextAsync(Engagement, CancellationToken.None));
    }

    [Fact]
    public async Task GetDynamicContextSnapshotAsync_PinnedEpoch_ReadsThatEpochAndIgnoresThePointer()
    {
        var documents = new FakeContextDocuments();
        var store = new CosmosEngagementContextStore(documents);
        await store.UpsertDynamicContextAsync(Engagement, "one", CancellationToken.None);
        await store.UpsertDynamicContextAsync(Engagement, "two", CancellationToken.None);

        Assert.Equal("one", (await store.GetDynamicContextSnapshotAsync(Engagement, 0, CancellationToken.None))!.Content);
    }

    [Fact]
    public async Task GetDynamicContextAsync_PointerNamesAMissingEpoch_IsNull()
    {
        var store = new CosmosEngagementContextStore(new FakeContextDocuments { Pointer = Pointer(5) });

        Assert.Null(await store.GetDynamicContextAsync(Engagement, CancellationToken.None));
    }

    [Fact]
    public async Task MergeDynamicContextAsync_KeepsTheKeysTheRefreshDidNotWrite()
    {
        var documents = new FakeContextDocuments();
        var store = new CosmosEngagementContextStore(documents);
        await store.UpsertDynamicContextAsync(Engagement, "{\"engagement_brief\":\"b\"}", CancellationToken.None);

        var epoch = await store.MergeDynamicContextAsync(Engagement, new Dictionary<string, string> { ["client_profile"] = "\"c\"" }, CancellationToken.None);

        Assert.Equal(1, epoch);
        var merged = await store.GetDynamicContextAsync(Engagement, CancellationToken.None);
        Assert.Contains("engagement_brief", merged, StringComparison.Ordinal);
        Assert.Contains("client_profile", merged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidArguments_Throw()
    {
        var store = new CosmosEngagementContextStore(new FakeContextDocuments());

        await Assert.ThrowsAsync<ArgumentException>(() => store.GetDynamicContextSnapshotAsync(" ", null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertDynamicContextAsync(" ", "x", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.UpsertDynamicContextAsync(Engagement, null!, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => new CosmosEngagementContextStore((IEngagementContextDocuments)null!));
        Assert.Throws<ArgumentNullException>(() => new CosmosEngagementContextStore((Container)null!));
    }

    private static EngagementContextPointer Pointer(int epoch) =>
        new(CosmosEngagementContextStore.PointerId(Engagement), Engagement, epoch, "hash", DateTime.UnixEpoch);

    /// <summary>In-memory pointer and epochs; <see cref="EpochTaken"/> makes every epoch create lose the race.</summary>
    private sealed class FakeContextDocuments : IEngagementContextDocuments
    {
        public EngagementContextPointer? Pointer { get; set; }

        public bool EpochTaken { get; init; }

        public int PointerWrites { get; private set; }

        public List<EngagementContextEpoch> Epochs { get; } = [];

        public Task<EngagementContextPointer?> ReadPointerAsync(EngagementId engagementId, CancellationToken ct) => Task.FromResult(Pointer);

        public Task<EngagementContextEpoch?> ReadEpochAsync(EngagementId engagementId, string epochId, CancellationToken ct) =>
            Task.FromResult(Epochs.SingleOrDefault(e => e.Id == epochId));

        public Task<bool> TryCreateEpochAsync(EngagementContextEpoch epoch, CancellationToken ct)
        {
            if (EpochTaken)
            {
                return Task.FromResult(false);
            }

            Epochs.Add(epoch);
            return Task.FromResult(true);
        }

        public Task UpsertPointerAsync(EngagementContextPointer pointer, CancellationToken ct)
        {
            PointerWrites++;
            Pointer = pointer;
            return Task.CompletedTask;
        }
    }
}
