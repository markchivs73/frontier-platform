namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S13.66 <see cref="IKeyProvider"/> test double modelling key rotation (doc 05 §5): it holds
/// every key version it has ever issued and resolves an old version by id forever, while
/// <see cref="GetCurrentKeyAsync"/> returns only the version currently enabled for signing.
/// <see cref="GetKeyCallCount"/> pins the "one call per distinct key id" claim.
/// </summary>
internal sealed class FakeRotatingKeyProvider : IKeyProvider
{
    internal static readonly SigningKey V1 = new("dev-key/v1", "frontier-dev-signing-key-v1"u8.ToArray());
    internal static readonly SigningKey V2 = new("dev-key/v2", "frontier-dev-signing-key-v2"u8.ToArray());
    internal static readonly SigningKey V3 = new("dev-key/v3", "frontier-dev-signing-key-v3"u8.ToArray());

    private readonly Dictionary<string, SigningKey> keys = new(StringComparer.Ordinal) { [V1.KeyId] = V1, [V2.KeyId] = V2 };

    private SigningKey current = V1;

    /// <summary>How many times <see cref="GetKeyAsync"/> has been called — a resolver must not call it per record.</summary>
    internal int GetKeyCallCount { get; private set; }

    /// <summary>Retires the current version for signing and enables <paramref name="key"/>, retaining the old one for verify.</summary>
    internal void RotateTo(SigningKey key)
    {
        keys[key.KeyId] = key;
        current = key;
    }

    /// <summary>Makes <paramref name="keyId"/> unresolvable — a version that has been destroyed rather than retained.</summary>
    internal void Forget(string keyId) => keys.Remove(keyId);

    /// <inheritdoc />
    public Task<SigningKey> GetCurrentKeyAsync(CancellationToken cancellationToken) => Task.FromResult(current);

    /// <inheritdoc />
    public Task<SigningKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        GetKeyCallCount++;
        return Task.FromResult(keys.TryGetValue(keyId, out var key) ? key : null);
    }
}
