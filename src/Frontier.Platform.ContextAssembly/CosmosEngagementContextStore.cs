using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Cosmos-backed <see cref="IEngagementContextStore"/> (S6.2a, doc 04 §6): reads/writes
/// dynamic context via the <c>engagement-context</c> container with epoch-based versioning.
/// Each upsert increments the epoch and updates the mutable `:current` pointer.
/// </summary>
/// <remarks>
/// The decisions — epoch numbering, document ids, what a lost race returns — live here and are unit-tested;
/// the SDK calls are <see cref="CosmosEngagementContextDocuments"/> (S13.193).
/// </remarks>
internal sealed class CosmosEngagementContextStore : IEngagementContextStore
{
    private readonly IEngagementContextDocuments documents;

    /// <summary>The Cosmos container name (<c>engagement-context</c>).</summary>
    internal const string ContainerName = "engagement-context";

    /// <summary>Suffix for the current pointer document id: <c>{engagementId}:ctx:current</c>.</summary>
    private const string CurrentPointerSuffix = ":ctx:current";

    /// <summary>Prefix for epoch documents: <c>{engagementId}:ctx:e{epoch:D6}</c>.</summary>
    private const string EpochPrefix = ":ctx:e";

    /// <summary>Initializes the store with a reference to the engagement-context container.</summary>
    public CosmosEngagementContextStore(Container container)
        : this(new CosmosEngagementContextDocuments(container))
    {
    }

    /// <summary>Initializes the store over <paramref name="documents"/>.</summary>
    internal CosmosEngagementContextStore(IEngagementContextDocuments documents)
    {
        this.documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    /// <summary>The <c>:current</c> pointer's document id.</summary>
    internal static string PointerId(EngagementId engagementId) => $"{engagementId}{CurrentPointerSuffix}";

    /// <summary>The document id of epoch <paramref name="epoch"/>.</summary>
    internal static string EpochId(EngagementId engagementId, int epoch) => $"{engagementId}{EpochPrefix}{epoch:D6}";

    /// <inheritdoc />
    public async Task<string?> GetDynamicContextAsync(EngagementId engagementId, CancellationToken ct) =>
        (await GetDynamicContextSnapshotAsync(engagementId, null, ct))?.Content;

    /// <inheritdoc />
    public async Task<EngagementContextSnapshot?> GetDynamicContextSnapshotAsync(EngagementId engagementId, int? epoch, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engagementId);

        var resolvedEpoch = epoch ?? await ReadCurrentEpochAsync(engagementId, ct);
        if (resolvedEpoch is null)
            return null;

        var doc = await documents.ReadEpochAsync(engagementId, EpochId(engagementId, resolvedEpoch.Value), ct);
        return doc is null ? null : new EngagementContextSnapshot(doc.Epoch, doc.Id, doc.ContentHash, doc.Content);
    }

    /// <summary>Reads the <c>:current</c> pointer's epoch, or <see langword="null"/> when the engagement has no context.</summary>
    internal async Task<int?> ReadCurrentEpochAsync(EngagementId engagementId, CancellationToken ct) =>
        (await documents.ReadPointerAsync(engagementId, ct))?.Epoch;

    /// <inheritdoc />
    /// <remarks>
    /// Read-merge-write over the epoch documents, not an in-place patch: the epoch history is
    /// append-only (doc 04 §6), so a scoped refresh still lands as a whole new epoch — it just
    /// carries the keys it did not change forward instead of dropping them. Concurrency is the
    /// upsert's existing conflict handling; a single live instance per engagement-workflow (hard
    /// invariant 3) is what keeps that sufficient in Phase 1.
    /// </remarks>
    public Task<int> MergeDynamicContextAsync(EngagementId engagementId, IReadOnlyDictionary<string, string> components, CancellationToken ct) =>
        EngagementContextMerge.ApplyThroughAsync(this, engagementId, components, ct);

    /// <inheritdoc />
    /// <remarks>
    /// A concurrent writer that already created the next epoch wins: this write is dropped and the epoch the
    /// pointer names is returned (0 when the winner has not moved the pointer yet).
    /// </remarks>
    public async Task<int> UpsertDynamicContextAsync(EngagementId engagementId, string dynamicContent, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engagementId);
        ArgumentNullException.ThrowIfNull(dynamicContent);

        var contentHash = CanonicalProfile.Hash(dynamicContent);
        var current = await ReadCurrentEpochAsync(engagementId, ct);
        var nextEpoch = current is null ? 0 : current.Value + 1;
        var epochDoc = new EngagementContextEpoch(EpochId(engagementId, nextEpoch), engagementId, nextEpoch, contentHash, dynamicContent, DateTime.UtcNow);
        if (!await documents.TryCreateEpochAsync(epochDoc, ct))
            return await ReadCurrentEpochAsync(engagementId, ct) ?? 0;

        await documents.UpsertPointerAsync(new EngagementContextPointer(PointerId(engagementId), engagementId, nextEpoch, contentHash, DateTime.UtcNow), ct);
        return nextEpoch;
    }
}
