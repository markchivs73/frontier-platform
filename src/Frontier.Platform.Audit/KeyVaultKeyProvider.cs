using System.Collections.Concurrent;

namespace Frontier.Platform.Audit;

/// <summary>
/// The deployed-profile <see cref="IKeyProvider"/> (ADR-PA33): resolves ES256 <em>public</em> key
/// versions from Key Vault so stored records can be verified locally, by the version each record
/// names (ADR-PA22).
///
/// <para>
/// Resolved versions are cached for the process lifetime and the cache is never invalidated, which
/// is safe precisely because a Key Vault key version is immutable — its public part cannot change,
/// only its enabled state can, and that governs signing rather than verification. The cache is what
/// makes verifying a long chain cost one vault call per <em>distinct</em> key version, on top of
/// <see cref="SigningKeyResolver"/>'s one call per distinct id per verification.
/// </para>
/// </summary>
internal sealed class KeyVaultKeyProvider(IKeyVaultSigningClient client) : IKeyProvider
{
    private readonly ConcurrentDictionary<string, SigningKey> cache = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<SigningKey> GetCurrentKeyAsync(CancellationToken cancellationToken)
    {
        // Deliberately not cached: "current" changes on rotation, and a process that cached it would
        // keep signing under a retired version until it restarted.
        var current = await client.GetCurrentAsync(cancellationToken);
        var key = ToSigningKey(current);
        cache[key.KeyId] = key;

        return key;
    }

    /// <inheritdoc />
    public async Task<SigningKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(keyId, out var cached))
        {
            return cached;
        }

        if (await client.GetByIdAsync(keyId, cancellationToken) is not { } resolved)
        {
            // A destroyed version, a forged id, or one this vault never issued. Absent from the map
            // means the record fails closed and the id is reported in UnresolvedKeyIds (ADR-PA22) —
            // never a silent fall back to the current key.
            return null;
        }

        var key = ToSigningKey(resolved);
        cache[key.KeyId] = key;

        return key;
    }

    /// <summary>Projects a resolved vault key version onto the verification-side <see cref="SigningKey"/>.</summary>
    internal static SigningKey ToSigningKey(KeyVaultPublicKey resolved) =>
        SigningKey.ForEs256(resolved.KeyId, resolved.PublicKeyDer);
}
