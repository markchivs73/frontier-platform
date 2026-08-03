namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>Tests for <see cref="Phase1BaselineCatalogueStore"/> (S4.2).</summary>
public sealed class Phase1BaselineCatalogueStoreTests
{
    [Fact]
    public async Task GetBaselineCatalogueAsync_KnownCatalogueId_ReturnsBaselineCatalogueJson()
    {
        var store = new Phase1BaselineCatalogueStore();

        var json = await store.GetBaselineCatalogueAsync(Phase1ContextCatalogue.BaselineCatalogueId, CancellationToken.None);

        Assert.Equal(Phase1ContextCatalogue.BaselineCatalogueJson, json);
    }

    [Fact]
    public async Task GetBaselineCatalogueAsync_UnknownCatalogueId_ReturnsNull()
    {
        var store = new Phase1BaselineCatalogueStore();

        var json = await store.GetBaselineCatalogueAsync("unknown-catalogue", CancellationToken.None);

        Assert.Null(json);
    }

    [Fact]
    public async Task GetBaselineCatalogueAsync_WhitespaceCatalogueId_Throws()
    {
        var store = new Phase1BaselineCatalogueStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.GetBaselineCatalogueAsync(" ", CancellationToken.None));
    }
}
