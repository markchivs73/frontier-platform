namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// An in-memory Key Vault standing behind <see cref="IKeyVaultSigningClient"/> (ADR-PA33 tests):
/// real P-256 signing via <see cref="TestEs256Key"/>, versions that can be rotated and destroyed,
/// and call counters that pin the caching claims. No Azure, no network, no resources created.
/// </summary>
internal sealed class FakeKeyVaultSigningClient : IKeyVaultSigningClient
{
    internal const string V1 = "https://frontier-kv.vault.azure.net/keys/audit/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string V2 = "https://frontier-kv.vault.azure.net/keys/audit/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly HashSet<string> destroyed = new(StringComparer.Ordinal);

    private string currentKeyId = V1;

    /// <summary>How many times a specific version was fetched — the cache must make this one per version.</summary>
    internal int GetByIdCallCount { get; private set; }

    /// <summary>How many times the current version was fetched.</summary>
    internal int GetCurrentCallCount { get; private set; }

    /// <summary>Set to make every call throw, standing in for an unreachable vault.</summary>
    internal Exception? Failure { get; set; }

    /// <summary>Retires the current version in favour of <paramref name="keyId"/>, which is what a rotation does.</summary>
    internal void RotateTo(string keyId) => currentKeyId = keyId;

    /// <summary>Makes <paramref name="keyId"/> unresolvable — destroyed or purged, not merely disabled.</summary>
    internal void Destroy(string keyId) => destroyed.Add(keyId);

    /// <inheritdoc />
    public Task<KeyVaultPublicKey> GetCurrentAsync(CancellationToken cancellationToken)
    {
        GetCurrentCallCount++;
        ThrowIfFailing();

        return Task.FromResult(new KeyVaultPublicKey(currentKeyId, TestEs256Key.PublicKeyDer(currentKeyId)));
    }

    /// <inheritdoc />
    public Task<KeyVaultPublicKey?> GetByIdAsync(string keyId, CancellationToken cancellationToken)
    {
        GetByIdCallCount++;
        ThrowIfFailing();

        return Task.FromResult(destroyed.Contains(keyId)
            ? null
            : new KeyVaultPublicKey(keyId, TestEs256Key.PublicKeyDer(keyId)));
    }

    /// <inheritdoc />
    public Task<byte[]> SignAsync(string keyId, byte[] digest, CancellationToken cancellationToken)
    {
        ThrowIfFailing();

        return Task.FromResult(TestEs256Key.SignDigest(keyId, digest));
    }

    private void ThrowIfFailing()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
