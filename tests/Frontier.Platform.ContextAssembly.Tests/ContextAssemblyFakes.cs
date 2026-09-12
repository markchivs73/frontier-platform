using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>In-memory <see cref="IBaselineCatalogueStore"/> double.</summary>
internal sealed class FakeBaselineCatalogueStore(string? content) : IBaselineCatalogueStore
{
    public string? RequestedCatalogueId { get; private set; }

    public Task<string?> GetBaselineCatalogueAsync(string catalogueId, CancellationToken ct)
    {
        RequestedCatalogueId = catalogueId;
        return Task.FromResult(content);
    }
}

/// <summary>In-memory <see cref="IEngagementContextStore"/> double.</summary>
internal sealed class FakeEngagementContextStore(string? content) : IEngagementContextStore
{
    private int currentEpoch;

    public EngagementId RequestedEngagementId { get; private set; } = "";

    public Task<string?> GetDynamicContextAsync(EngagementId engagementId, CancellationToken ct)
    {
        RequestedEngagementId = engagementId;
        return Task.FromResult(content);
    }

    public Task<EngagementContextSnapshot?> GetDynamicContextSnapshotAsync(EngagementId engagementId, int? epoch, CancellationToken ct)
    {
        RequestedEngagementId = engagementId;
        if (content is null || (epoch is not null && epoch.Value != currentEpoch))
            return Task.FromResult<EngagementContextSnapshot?>(null);
        return Task.FromResult<EngagementContextSnapshot?>(new EngagementContextSnapshot(
            currentEpoch, $"{engagementId.Value}:ctx:e{currentEpoch:D6}", Frontier.Platform.Serialization.CanonicalProfile.Hash(content), content));
    }

    public Task<int> UpsertDynamicContextAsync(EngagementId engagementId, string dynamicContent, CancellationToken ct)
    {
        return Task.FromResult(++currentEpoch);
    }

    /// <summary>
    /// This double serves a fixed <c>content</c> and cannot represent a write, so it refuses the
    /// merge rather than reporting a success that changed nothing (S13.62). No assembler test
    /// merges; <c>EngagementContextMergeTests</c> exercises the real stores.
    /// </summary>
    public Task<int> MergeDynamicContextAsync(EngagementId engagementId, IReadOnlyDictionary<string, string> components, CancellationToken ct) =>
        throw new NotSupportedException("FakeEngagementContextStore serves fixed content; merge is exercised against the real stores.");
}

/// <summary>
/// <see cref="ICachingStrategyRegistry"/> double that can return a null strategy,
/// which <see cref="CachingStrategyRegistry"/> never does but the interface allows.
/// </summary>
internal sealed class FakeCachingStrategyRegistry(ICachingStrategy? resolved) : ICachingStrategyRegistry
{
    public ICachingStrategy? Resolve(string provider, string modelId, string? modelVersion = null) => resolved;

    public ICachingStrategy? ResolveStrategy(string provider) => resolved;
}
