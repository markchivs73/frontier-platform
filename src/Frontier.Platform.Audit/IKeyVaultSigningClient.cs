using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Audit;

/// <summary>
/// The consumer-owned port onto Azure Key Vault's key operations (ADR-PA33, doc 00 §9: every SDK
/// dependency sits behind an interface this library owns, with no SDK type on it). Exactly three
/// operations, because that is all audit signing needs; anything wider would be a Key Vault client
/// wearing an interface rather than a seam.
/// </summary>
internal interface IKeyVaultSigningClient
{
    /// <summary>
    /// The key's current version — its full versioned identifier and public part. This is the
    /// version new records are signed under, and the one the boot check probes.
    /// </summary>
    Task<KeyVaultPublicKey> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The version named by <paramref name="keyId"/>, or <see langword="null"/> when it cannot be
    /// resolved. A <em>disabled</em> version resolves normally and must keep doing so: doc 05 §5
    /// disables old versions for signing and retains them for verify, so treating disabled as
    /// unresolvable would report every pre-rotation record as unverifiable (ADR-PA22).
    /// </summary>
    Task<KeyVaultPublicKey?> GetByIdAsync(string keyId, CancellationToken cancellationToken);

    /// <summary>
    /// Signs <paramref name="digest"/> with <paramref name="keyId"/> inside Key Vault (ES256) and
    /// returns the raw <c>r ‖ s</c> signature. The private key is never returned by this or any
    /// other member — there is deliberately no "get the key material" operation on this port.
    /// </summary>
    Task<byte[]> SignAsync(string keyId, byte[] digest, CancellationToken cancellationToken);
}

/// <summary>A Key Vault key version's identifier and its public part as a DER SubjectPublicKeyInfo (ADR-PA33).</summary>
[ExcludeFromCodeCoverage(Justification = "Record/POCO with assignment-only constructor")]
internal sealed record KeyVaultPublicKey(string KeyId, ReadOnlyMemory<byte> PublicKeyDer);
