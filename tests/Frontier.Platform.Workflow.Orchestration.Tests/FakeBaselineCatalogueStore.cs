using Frontier.Platform.ContextAssembly;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Configurable <see cref="IBaselineCatalogueStore"/> test double for S4.2 composer tests.</summary>
internal sealed class FakeBaselineCatalogueStore(string? catalogueJson) : IBaselineCatalogueStore
{
    public Task<string?> GetBaselineCatalogueAsync(string catalogueId, CancellationToken ct) => Task.FromResult(catalogueJson);
}
