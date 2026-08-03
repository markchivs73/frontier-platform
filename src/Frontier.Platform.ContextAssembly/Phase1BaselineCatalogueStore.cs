namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Serves <see cref="Phase1ContextCatalogue.BaselineCatalogueJson"/> for the single PoC
/// baseline catalogue (<see cref="IBaselineCatalogueStore"/> registered by
/// <see cref="ContextAssemblyServiceCollectionExtensions.AddFrontierContextAssembly"/>).
/// A Cosmos-backed implementation (config-store conventions) replaces this once a second
/// baseline catalogue is needed.
/// </summary>
internal sealed class Phase1BaselineCatalogueStore : IBaselineCatalogueStore
{
    /// <inheritdoc />
    public Task<string?> GetBaselineCatalogueAsync(string catalogueId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogueId);

        var json = catalogueId == Phase1ContextCatalogue.BaselineCatalogueId
            ? Phase1ContextCatalogue.BaselineCatalogueJson
            : null;

        return Task.FromResult(json);
    }
}
