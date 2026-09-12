namespace Frontier.Platform.Audit.Tests;

/// <summary>S13.66 tests for <see cref="DevKeyProvider"/>'s by-id resolution (doc 05 §5).</summary>
public sealed class DevKeyProviderTests
{
    [Fact]
    public async Task GetKeyAsync_KnownId_ReturnsTheDevKey()
    {
        var provider = new DevKeyProvider();
        var current = await provider.GetCurrentKeyAsync(CancellationToken.None);

        var resolved = await provider.GetKeyAsync(current.KeyId, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(current.KeyId, resolved.KeyId);
        Assert.True(current.KeyMaterial.Span.SequenceEqual(resolved.KeyMaterial.Span));
    }

    [Fact]
    public async Task GetKeyAsync_UnknownId_ReturnsNull()
    {
        // Guards the "return the fixed key whatever the id" shortcut: an id this provider never
        // issued must not resolve, or a forged SigningKeyId would silently verify.
        var provider = new DevKeyProvider();

        Assert.Null(await provider.GetKeyAsync("dev-key/v99", CancellationToken.None));
    }
}
