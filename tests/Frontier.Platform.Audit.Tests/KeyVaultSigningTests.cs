namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S13.107 tests for the Key Vault signing profile (ADR-PA33): version resolution, caching,
/// rotation, and the fail-closed answer for a version that no longer exists. No live Azure — the
/// SDK sits behind <see cref="IKeyVaultSigningClient"/> and <see cref="FakeKeyVaultSigningClient"/>
/// stands in for it with real P-256 keys.
/// </summary>
public sealed class KeyVaultSigningTests
{
    private const string RecordHash = "3F786850E387550FDAB836ED7E6DC881DE23001B";

    [Fact]
    public async Task GetCurrentKeyAsync_ReturnsTheCurrentVersionAsAnEs256PublicKey()
    {
        var provider = new KeyVaultKeyProvider(new FakeKeyVaultSigningClient());

        var key = await provider.GetCurrentKeyAsync(CancellationToken.None);

        Assert.Equal(FakeKeyVaultSigningClient.V1, key.KeyId);
        Assert.Equal(SigningAlgorithm.Es256, key.Algorithm);
        Assert.Equal(TestEs256Key.PublicKeyDer(FakeKeyVaultSigningClient.V1), key.KeyMaterial.ToArray());
    }

    [Fact]
    public async Task GetKeyAsync_ResolvesAVersionById()
    {
        var provider = new KeyVaultKeyProvider(new FakeKeyVaultSigningClient());

        var key = await provider.GetKeyAsync(FakeKeyVaultSigningClient.V2, CancellationToken.None);

        Assert.NotNull(key);
        Assert.Equal(FakeKeyVaultSigningClient.V2, key.KeyId);
    }

    [Fact]
    public async Task GetKeyAsync_SameVersionTwice_FetchesItOnce()
    {
        var vault = new FakeKeyVaultSigningClient();
        var provider = new KeyVaultKeyProvider(vault);

        await provider.GetKeyAsync(FakeKeyVaultSigningClient.V2, CancellationToken.None);
        await provider.GetKeyAsync(FakeKeyVaultSigningClient.V2, CancellationToken.None);

        Assert.Equal(1, vault.GetByIdCallCount);
    }

    [Fact]
    public async Task GetKeyAsync_AfterGetCurrent_ServesTheCurrentVersionFromTheCache()
    {
        var vault = new FakeKeyVaultSigningClient();
        var provider = new KeyVaultKeyProvider(vault);

        await provider.GetCurrentKeyAsync(CancellationToken.None);
        await provider.GetKeyAsync(FakeKeyVaultSigningClient.V1, CancellationToken.None);

        Assert.Equal(0, vault.GetByIdCallCount);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_AfterRotation_ReturnsTheNewVersionRatherThanACachedOne()
    {
        // Caching "current" would keep a long-running process signing under a retired version until
        // it restarted, which is the rotation failure this guards.
        var vault = new FakeKeyVaultSigningClient();
        var provider = new KeyVaultKeyProvider(vault);
        await provider.GetCurrentKeyAsync(CancellationToken.None);

        vault.RotateTo(FakeKeyVaultSigningClient.V2);

        Assert.Equal(FakeKeyVaultSigningClient.V2, (await provider.GetCurrentKeyAsync(CancellationToken.None)).KeyId);
    }

    [Fact]
    public async Task GetKeyAsync_DestroyedVersion_ReturnsNullRatherThanFallingBackToCurrent()
    {
        // ADR-PA22 verbatim: a version that cannot be resolved is null, never the current key.
        var vault = new FakeKeyVaultSigningClient();
        vault.Destroy(FakeKeyVaultSigningClient.V2);

        Assert.Null(await new KeyVaultKeyProvider(vault).GetKeyAsync(FakeKeyVaultSigningClient.V2, CancellationToken.None));
    }

    [Fact]
    public async Task SignAsync_SignsWithTheCurrentVersionAndTheSignatureVerifiesAgainstIt()
    {
        var vault = new FakeKeyVaultSigningClient();
        var signing = new KeyVaultAuditSigningService(vault);

        var signature = await signing.SignAsync(RecordHash, CancellationToken.None);

        Assert.Equal(FakeKeyVaultSigningClient.V1, signature.KeyId);
        Assert.True(AuditSignatureVerifier.Verify(RecordHash, signature.Signature, TestEs256Key.VerificationKey(FakeKeyVaultSigningClient.V1)));
    }

    [Fact]
    public async Task SignAsync_AfterRotation_SignsWithTheNewVersion()
    {
        var vault = new FakeKeyVaultSigningClient();
        var signing = new KeyVaultAuditSigningService(vault);
        vault.RotateTo(FakeKeyVaultSigningClient.V2);

        var signature = await signing.SignAsync(RecordHash, CancellationToken.None);

        Assert.Equal(FakeKeyVaultSigningClient.V2, signature.KeyId);
        Assert.True(AuditSignatureVerifier.Verify(RecordHash, signature.Signature, TestEs256Key.VerificationKey(FakeKeyVaultSigningClient.V2)));
    }

    [Fact]
    public async Task GetCurrentKeyIdAsync_ReturnsTheVersionSignAsyncWouldUse()
    {
        var signing = new KeyVaultAuditSigningService(new FakeKeyVaultSigningClient());

        Assert.Equal(FakeKeyVaultSigningClient.V1, await signing.GetCurrentKeyIdAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SignAsync_VaultUnreachable_PropagatesRatherThanReturningAnUnsignedRecord()
    {
        var vault = new FakeKeyVaultSigningClient { Failure = new HttpRequestException("vault down") };

        await Assert.ThrowsAsync<HttpRequestException>(
            () => new KeyVaultAuditSigningService(vault).SignAsync(RecordHash, CancellationToken.None));
    }

    [Fact]
    public async Task HmacSigningService_SignsWithTheCurrentKeyAndVerifies()
    {
        var provider = new DevKeyProvider();
        var signing = new HmacAuditSigningService(provider);

        var signature = await signing.SignAsync(RecordHash, CancellationToken.None);
        var key = await provider.GetCurrentKeyAsync(CancellationToken.None);

        Assert.Equal(key.KeyId, signature.KeyId);
        Assert.Equal(key.KeyId, await signing.GetCurrentKeyIdAsync(CancellationToken.None));
        Assert.True(AuditSignatureVerifier.Verify(RecordHash, signature.Signature, key));
    }
}
